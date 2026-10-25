using System.Net;
using System.Net.Http.Json;
using System.Text;
using MatchShift.Api;
using MatchShift.Api.Playback;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MatchShift.Api.Tests;

public class ApiSecurityTests
{
    [Fact]
    public async Task One_client_cannot_occupy_all_sessions_and_existing_session_still_works()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var first = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/demo/events?session={first}")).StatusCode);
        for (var i = 1; i < SessionRegistry.MaxSessionsPerClient; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/demo/events?session={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync($"/api/demo/events?session={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/demo/events?session={first}")).StatusCode);
    }

    [Fact]
    public void Session_limits_are_per_client_and_global_capacity_is_a_controlled_503()
    {
        using var factory = new WebApplicationFactory<Program>();
        var registry = factory.Services.GetRequiredService<SessionRegistry>();
        for (var i = 0; i < 32; i++) registry.Resolve(Context($"192.0.2.{i / 4 + 1}"));
        var error = Assert.Throws<BadHttpRequestException>(() => registry.Resolve(Context("192.0.2.99")));
        Assert.Equal(503, error.StatusCode);
    }

    [Theory]
    [InlineData("team=not-a-team")]
    [InlineData("player=not-a-player")]
    [InlineData("mode=999")]
    [InlineData("mode=Analyst&mode=Studio")]
    [InlineData("session=invalid")]
    public async Task Invalid_profiles_and_session_ids_are_rejected(string query)
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/demo/state?" + query)).StatusCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(121)]
    [InlineData(1e100)]
    public async Task Seek_rejects_values_outside_the_bounded_match_range(double minute)
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/demo/seek", new { minute })).StatusCode);
    }

    [Fact]
    public async Task Body_limit_rejects_fixed_length_and_chunked_requests_before_session_allocation()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var bytes = Encoding.UTF8.GetBytes("{\"matchId\":\"" + new string('x', 5000) + "\"}");
        using var fixedLength = new ByteArrayContent(bytes);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/api/demo/reset", fixedLength)).StatusCode);
        using var content = new StreamContent(new MemoryStream(bytes));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/demo/reset") { Content = content };
        request.Headers.TransferEncodingChunked = true;
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.SendAsync(request)).StatusCode);
        Assert.Empty(factory.Services.GetRequiredService<SessionRegistry>().All);
    }

    [Fact]
    public async Task Repeated_expensive_requests_get_429_without_blocking_read_only_status()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        for (var i = 0; i < 20; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/demo/seek", new { minute = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/demo/seek", new { minute = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/agents/status")).StatusCode);
    }

    [Fact]
    public async Task Concurrency_is_limited_per_client_and_static_judge_is_not_in_the_api_budget()
    {
        using var factory = new WebApplicationFactory<Program>();
        var limiter = factory.Services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter!;
        var leases = new List<System.Threading.RateLimiting.RateLimitLease>();
        try
        {
            for (var i = 0; i < 8; i++)
            {
                var lease = await limiter.AcquireAsync(Context("192.0.2.1"));
                leases.Add(lease);
                Assert.True(lease.IsAcquired);
            }
            using var ninth = await limiter.AcquireAsync(Context("192.0.2.1"));
            Assert.False(ninth.IsAcquired);
            using var otherClient = await limiter.AcquireAsync(Context("192.0.2.2"));
            Assert.True(otherClient.IsAcquired);
            var judge = Context("192.0.2.1"); judge.Request.Path = "/";
            judge.Request.QueryString = new QueryString("?demo=judge");
            using var staticLease = await limiter.AcquireAsync(judge);
            Assert.True(staticLease.IsAcquired);
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }

    [Fact]
    public async Task Production_has_security_headers_and_no_default_localhost_cors_permission()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Production"));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://matchshift.test") });
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        using var response = await client.GetAsync("/api/meta");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Contains("frame-ancestors 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    private static DefaultHttpContext Context(string address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        context.Request.Path = "/api/demo/events";
        return context;
    }
}
