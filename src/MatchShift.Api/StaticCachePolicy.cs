namespace MatchShift.Api;

/// <summary>
/// Browser caching for the web app's static files. Vite's fingerprinted build output (<c>/assets/name-HASH.js</c>) never
/// changes under the same name, so it may be cached for a year. Everything else (index.html, the crowd audio clips,
/// other public files) keeps a stable name across deployments, so browsers must re-check it every time ("no-cache":
/// revalidate with the ETag; an unchanged file costs a 304). Without this, a browser may keep using a replaced file -
/// for example an old crowd-goal.wav - long after a redeploy.
/// </summary>
public static class StaticCachePolicy
{
    public const string Immutable = "public, max-age=31536000, immutable";
    public const string Revalidate = "no-cache";

    public static string For(string requestPath) =>
        requestPath.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase) ? Immutable : Revalidate;
}
