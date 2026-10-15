using System.Diagnostics.Metrics;
using Azure.Monitor.OpenTelemetry.AspNetCore;

namespace MatchShift.Api;

/// <summary>Custom MatchShift metrics, exported to Application Insights when configured.</summary>
public static class MatchShiftMetrics
{
    public const string MeterName = "MatchShift";
    private static readonly Meter Meter = new(MeterName, "1.0");

    public static readonly Counter<long> InsightsRaised = Meter.CreateCounter<long>("matchshift.insights.raised", description: "Insights raised by the live detector");
    public static readonly Counter<long> SnapshotsServed = Meter.CreateCounter<long>("matchshift.snapshots.served", description: "Live snapshots served");
    public static readonly Counter<long> NarrativesFallback = Meter.CreateCounter<long>("matchshift.narratives.fallback", description: "AI narratives replaced by template text");
}

public static class TelemetryExtensions
{
    /// <summary>
    /// Wires Azure Monitor (Application Insights) via OpenTelemetry when
    /// APPLICATIONINSIGHTS_CONNECTION_STRING (or AzureMonitor:ConnectionString) is set. No-op otherwise.
    /// </summary>
    public static IServiceCollection AddMatchShiftTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] ?? configuration["AzureMonitor:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connection)) return services;

        services.AddOpenTelemetry()
            .UseAzureMonitor(o => o.ConnectionString = connection)
            .WithMetrics(m => m.AddMeter(MatchShiftMetrics.MeterName));
        return services;
    }
}
