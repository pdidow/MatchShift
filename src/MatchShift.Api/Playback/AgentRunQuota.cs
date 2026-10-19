using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MatchShift.Api.Playback;

public enum QuotaRefusal { None, Hourly, Daily, Client }

public sealed record QuotaStatus(int RunsLastHour, int RunsLast24Hours, int MaxPerHour, int MaxPerDay, int MaxPerClientPerHour, bool Durable)
{
    public bool Exhausted => RunsLastHour >= MaxPerHour || RunsLast24Hours >= MaxPerDay;
}

/// <summary>
/// Caps live agent workflow executions (each one is several Foundry model calls): a rolling hourly cap, a rolling
/// 24-hour cap and a per-client hourly cap. Only a run that will actually call Foundry is reserved: cached results,
/// polling and OFFLINE_TEMPLATE runs never consume quota.
///
/// The global run timestamps are persisted to a small JSON file when a state path is configured (on Azure App Service,
/// %HOME%\data is persistent, private and free), so the hourly and daily caps survive restarts and redeploys. Per-client
/// counts are in memory only and reset on restart.
/// </summary>
public sealed class AgentRunQuota
{
    private readonly object _gate = new();
    private readonly List<DateTimeOffset> _starts = [];
    private readonly Dictionary<string, List<DateTimeOffset>> _clients = [];
    private readonly TimeProvider _time;
    private readonly string? _statePath;
    private readonly ILogger? _logger;

    public AgentRunQuota(int maxPerHour, int maxPerDay, int maxPerClientPerHour, TimeProvider? time = null, string? statePath = null, ILogger? logger = null)
    {
        MaxPerHour = Math.Max(0, maxPerHour);
        MaxPerDay = Math.Max(0, maxPerDay);
        MaxPerClientPerHour = Math.Max(0, maxPerClientPerHour);
        _time = time ?? TimeProvider.System;
        _statePath = statePath;
        _logger = logger;
        Load();
    }

    public int MaxPerHour { get; }
    public int MaxPerDay { get; }
    public int MaxPerClientPerHour { get; }

    /// <summary>Reserves one live run for <paramref name="client"/>, or says which cap refused it. Nothing is reserved on refusal.</summary>
    public QuotaRefusal TryReserve(string client)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            Prune(now);
            if (_starts.Count(t => t > now.AddHours(-1)) >= MaxPerHour) return QuotaRefusal.Hourly;
            if (_starts.Count >= MaxPerDay) return QuotaRefusal.Daily;
            var key = ClientKey(client);
            if (!_clients.TryGetValue(key, out var mine)) _clients[key] = mine = [];
            if (mine.Count >= MaxPerClientPerHour) return QuotaRefusal.Client;
            _starts.Add(now);
            mine.Add(now);
            Save();
            return QuotaRefusal.None;
        }
    }

    public QuotaStatus Status()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            Prune(now);
            return new QuotaStatus(_starts.Count(t => t > now.AddHours(-1)), _starts.Count, MaxPerHour, MaxPerDay, MaxPerClientPerHour, _statePath is not null);
        }
    }

    private void Prune(DateTimeOffset now)
    {
        _starts.RemoveAll(t => t <= now.AddHours(-24));
        foreach (var (key, list) in _clients.ToArray())
        {
            list.RemoveAll(t => t <= now.AddHours(-1));
            if (list.Count == 0) _clients.Remove(key);
        }
    }

    /// <summary>Clients are only ever held as a short hash in memory; addresses are never stored or logged.</summary>
    private static string ClientKey(string client) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(client)))[..16];

    private sealed record State(List<DateTimeOffset> Starts);

    private void Load()
    {
        if (_statePath is null || !File.Exists(_statePath)) return;
        try
        {
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(_statePath));
            if (state?.Starts is { } starts) _starts.AddRange(starts);
            Prune(_time.GetUtcNow());
        }
        catch (Exception ex)
        {
            // A corrupt file must not disable the cap: start from the most conservative position for this hour.
            _logger?.LogWarning("Agent quota state unreadable ({FailureType}); treating this hour as used", ex.GetType().Name);
            var now = _time.GetUtcNow();
            _starts.AddRange(Enumerable.Repeat(now, MaxPerHour));
        }
    }

    private void Save()
    {
        if (_statePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            var temp = _statePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new State(_starts)));
            File.Move(temp, _statePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Agent quota state could not be saved ({FailureType}); in-memory caps still apply", ex.GetType().Name);
        }
    }

    /// <summary>Persistent, non-public location on Azure App Service (%HOME%\data); none elsewhere unless configured.</summary>
    public static string? DefaultStatePath(IConfiguration configuration)
    {
        if (configuration["AgentWorkflow:QuotaStatePath"] is { Length: > 0 } explicitPath) return explicitPath;
        var home = Environment.GetEnvironmentVariable("HOME");
        return Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") is not null && !string.IsNullOrEmpty(home)
            ? Path.Combine(home, "data", "matchshift", "agent-quota.json")
            : null;
    }
}
