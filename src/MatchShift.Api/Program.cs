using System.Text.Json.Serialization;
using MatchShift.Api.Endpoints;
using MatchShift.Api.Playback;
using MatchShift.Api;
using MatchShift.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<MatchCatalog>();
builder.Services.AddSingleton<NarrationQueue>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NarrationQueue>());
builder.Services.AddSingleton<SessionRegistry>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped(sp => sp.GetRequiredService<SessionRegistry>().Resolve(sp.GetRequiredService<IHttpContextAccessor>().HttpContext!));
builder.Services.AddHostedService<PlaybackService>();
builder.Services.AddMatchShiftNarration(builder.Configuration);
builder.Services.AddMatchShiftAgentWorkflow(builder.Configuration);
builder.Services.AddSingleton(sp =>
{
    var o = sp.GetRequiredService<MatchShift.Infrastructure.Foundry.AgentWorkflowOptions>();
    return new AgentRunQuota(o.MaxRunsPerHour, o.MaxRunsPerDay, o.MaxRunsPerClientPerHour, TimeProvider.System,
        AgentRunQuota.DefaultStatePath(builder.Configuration), sp.GetRequiredService<ILoggerFactory>().CreateLogger<AgentRunQuota>());
});
builder.Services.AddSingleton<AgentWorkflowService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AgentWorkflowService>());
builder.Services.AddMatchShiftTelemetry(builder.Configuration);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseDefaultFiles();
// Fingerprinted build assets are cached for good; index.html and stable-named files (crowd audio) are re-checked on every
// load, so a redeploy is picked up straight away (see StaticCachePolicy).
var staticFiles = new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = StaticCachePolicy.For(ctx.Context.Request.Path.Value ?? ""),
};
app.UseStaticFiles(staticFiles);

app.MapGet("/health", () => Results.Ok(new { status = "ok", synthetic = true }));
app.MapMatchShiftApi();
app.MapFallbackToFile("index.html", staticFiles);

app.Run();

public partial class Program;
