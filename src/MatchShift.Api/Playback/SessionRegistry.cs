namespace MatchShift.Api.Playback;

/// <summary>Process-local demo sessions. A random ID is a shareable playback capability, not an account.</summary>
public sealed class SessionRegistry(MatchCatalog catalog, NarrationQueue queue) : IDisposable
{
    private readonly Dictionary<string, DemoSession> _sessions = [];
    private readonly object _gate = new();
    public DemoSession Resolve(HttpContext http)
    {
        var supplied = http.Request.Query["session"].FirstOrDefault() ?? http.Request.Cookies["matchshift-session"];
        if (supplied is not null && !Guid.TryParseExact(supplied, "D", out _))
            throw new BadHttpRequestException("Invalid session ID.");
        var id = supplied ?? Guid.NewGuid().ToString();
        lock (_gate)
        {
            foreach (var old in _sessions.Where(kv => DateTime.UtcNow - kv.Value.LastAccess > TimeSpan.FromMinutes(30)).ToArray())
            { old.Value.Close(); _sessions.Remove(old.Key); }
            if (!_sessions.TryGetValue(id, out var session))
            {
                if (_sessions.Count >= 32) throw new BadHttpRequestException("Demo session capacity reached. Retry later.", 503);
                session = new DemoSession(catalog, queue) { Id = id };
                _sessions[id] = session;
            }
            session.Touch();
            if (supplied is null) http.Response.Cookies.Append("matchshift-session", id,
                new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = http.Request.IsHttps });
            return session;
        }
    }
    public IReadOnlyList<DemoSession> All { get { lock (_gate) return _sessions.Values.ToArray(); } }
    public void Dispose() { lock (_gate) { foreach (var session in _sessions.Values) session.Close(); _sessions.Clear(); } }
}
