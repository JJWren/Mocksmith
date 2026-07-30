using Mocksmith.Core.Generation;

namespace Mocksmith.Tests;

public class HtmlBridgeInjectorTests
{
    private const string SampleHtml = "<!doctype html><html><head></head><body><h1>Hi</h1></body></html>";

    [Fact]
    public void Inject_PlacesBridgeBeforeBodyClose()
    {
        var injected = HtmlBridgeInjector.Inject(SampleHtml);

        Assert.Contains("mocksmith-bridge", injected);
        Assert.True(
            injected.IndexOf("mocksmith-bridge", StringComparison.Ordinal)
            < injected.IndexOf("</body>", StringComparison.Ordinal));
    }

    [Fact]
    public void Inject_IsIdempotent()
    {
        var twice = HtmlBridgeInjector.Inject(HtmlBridgeInjector.Inject(SampleHtml));

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(twice, "id=\"mocksmith-bridge\""));
    }

    [Fact]
    public void Inject_WithoutBodyTag_Appends()
    {
        var injected = HtmlBridgeInjector.Inject("<div>fragment</div>");

        Assert.Contains("mocksmith-bridge", injected);
    }

    [Fact]
    public void BridgeScript_HasNoExternalReferences()
    {
        // The bridge itself must satisfy the same no-external-requests contract as samples.
        Assert.DoesNotContain("http://", HtmlBridgeInjector.BridgeScript);
        Assert.DoesNotContain("https://", HtmlBridgeInjector.BridgeScript);
    }
}
