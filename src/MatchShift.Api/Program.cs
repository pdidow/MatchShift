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
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok", synthetic = true }));
app.MapMatchShiftApi();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
