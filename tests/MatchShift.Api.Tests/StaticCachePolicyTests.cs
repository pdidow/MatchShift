namespace MatchShift.Api.Tests;

/// <summary>Browser caching of the web app's static files: replaced files must never be served stale after a redeploy.</summary>
public class StaticCachePolicyTests
{
    [Theory]
    [InlineData("/assets/index-Bi0t9Ypu.js")]
    [InlineData("/assets/ReplayView-_lekX8iG.js")]
    [InlineData("/assets/index-ABC123.css")]
    public void Fingerprinted_build_assets_are_cached_for_good(string path) =>
        Assert.Equal(StaticCachePolicy.Immutable, StaticCachePolicy.For(path));

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/audio/crowd-goal.wav")]
    [InlineData("/audio/crowd-chance.wav")]
    [InlineData("/audio/README.txt")]
    [InlineData("/broadcast")]
    public void Stable_named_files_are_revalidated_on_every_load(string path) =>
        Assert.Equal("no-cache", StaticCachePolicy.For(path));
}
