using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using MatchShift.Api.Speech;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MatchShift.Api.Tests;

public class SpeechTests
{
    [Theory]
    [InlineData("en-GB")]
    [InlineData("fr-FR")]
    [InlineData("es-ES")]
    [InlineData("de-DE")]
    [InlineData("it-IT")]
    [InlineData("pt-PT")]
    public void Fixed_voice_and_locale_and_escaped_text(string locale)
    {
        var request = new SpeechRequest("15 minutes <voice name='evil'> & 31%", locale);
        Assert.True(SpeechFallback.Valid(request));
        var document = XElement.Parse(SpeechFallback.Ssml(request));
        Assert.Equal(locale, document.Attribute(XNamespace.Xml + "lang")!.Value);
        var voice = Assert.Single(document.Elements());
        Assert.Equal(SpeechFallback.Voices[locale], voice.Attribute("name")!.Value);
        Assert.Empty(voice.Elements());
        Assert.Equal(request.Text, voice.Value);
    }

    [Theory]
    [InlineData("", "fr-FR")]
    [InlineData("15 minutes", "en")]
    [InlineData("15 minutes", "https://attacker.test")]
    public async Task Invalid_requests_do_not_call_Azure(string text, string locale)
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/speech", new { text, locale })).StatusCode);
    }

    [Fact]
    public async Task Disabled_service_is_explicit_and_oversized_text_rejected()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/speech", new { text = "15 minutes", locale = "fr-FR" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/speech", new { text = new string('a', 801), locale = "fr-FR" })).StatusCode);
    }
}
