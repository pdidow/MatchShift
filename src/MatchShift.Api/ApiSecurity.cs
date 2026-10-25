using System.Threading.RateLimiting;
using MatchShift.Agents.Narrative;
using MatchShift.Api.Playback;
using Microsoft.AspNetCore.Http.Features;

namespace MatchShift.Api;

/// <summary>Admission controls for the public demo. These are not authentication or a distributed spending limit.</summary>
public static class ApiSecurity
{
    public const int MaxBodyBytes = 4096;
    public const string ExpensivePolicy = "demo-expensive";
    public static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    private static bool IsApi(HttpContext context) => context.Request.Path.StartsWithSegments("/api");

    public static IServiceCollection AddDemoSecurity(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(c => !IsApi(c)
                    ? RateLimitPartition.GetNoLimiter("static")
                    : RateLimitPartition.GetConcurrencyLimiter("api", _ => new() { PermitLimit = 64, QueueLimit = 0 })),
                PartitionedRateLimiter.Create<HttpContext, string>(c => !IsApi(c)
                    ? RateLimitPartition.GetNoLimiter("static")
                    : RateLimitPartition.GetConcurrencyLimiter(ClientKey(c), _ => new() { PermitLimit = 8, QueueLimit = 0 })),
                PartitionedRateLimiter.Create<HttpContext, string>(c => !IsApi(c)
                    ? RateLimitPartition.GetNoLimiter("static")
                    : RateLimitPartition.GetFixedWindowLimiter(ClientKey(c), _ => new()
                    { PermitLimit = 240, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
            options.AddPolicy(ExpensivePolicy, c => RateLimitPartition.GetFixedWindowLimiter(ClientKey(c), _ => new()
                { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(180));
        return services;
    }

    /// <summary>Bound chunked bodies as well as Content-Length before endpoint binding/session allocation.</summary>
    public static async Task GuardRequest(HttpContext context, RequestDelegate next)
    {
        if (!IsApi(context)) { await next(context); return; }
        var request = context.Request;
        if (request.QueryString.Value?.Length > 1024 || request.Query.Count > 16 ||
            request.Query.Any(q => q.Value.Count != 1 || q.Value.ToString().Length > 128))
        { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        if (request.ContentLength > MaxBodyBytes)
        { context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; return; }

        using var body = new MemoryStream();
        var original = request.Body;
        try
        {
            if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true ||
                request.ContentLength > 0 || request.Headers.ContainsKey("Transfer-Encoding"))
            {
                var buffer = new byte[MaxBodyBytes + 1];
                var total = 0;
                while (total <= MaxBodyBytes)
                {
                    var read = await original.ReadAsync(buffer.AsMemory(total, buffer.Length - total), context.RequestAborted);
                    if (read == 0) break;
                    total += read;
                }
                if (total > MaxBodyBytes)
                { context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge; return; }
                body.Write(buffer, 0, total);
                body.Position = 0;
                request.Body = body;
            }
            await next(context);
        }
        catch (BadHttpRequestException e) when (!context.Response.HasStarted)
        {
            // Includes session admission failures during DI construction. Do not expose internal exception details.
            context.Response.StatusCode = e.StatusCode;
            await context.Response.WriteAsJsonAsync(new { message = "Request rejected; check input or retry later." }, context.RequestAborted);
        }
        finally { request.Body = original; }
    }

    public static ValueTask<object?> ValidateProfile(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var query = invocation.HttpContext.Request.Query;
        var session = invocation.HttpContext.RequestServices.GetRequiredService<DemoSession>();
        if ((query.TryGetValue("mode", out var mode) && (!Enum.TryParse<ViewerMode>(mode, true, out var m) || !Enum.IsDefined(m))) ||
            (query.TryGetValue("style", out var style) && (!Enum.TryParse<RecapStyle>(style, true, out var s) || !Enum.IsDefined(s))) ||
            (query.TryGetValue("team", out var team) && team != session.Match.HomeTeam.Id && team != session.Match.AwayTeam.Id) ||
            (query.TryGetValue("player", out var player) && session.Match.FindPlayer(player) is null))
            return ValueTask.FromResult<object?>(Results.BadRequest(new { message = "Invalid viewer profile for this match." }));
        return next(invocation);
    }
}
