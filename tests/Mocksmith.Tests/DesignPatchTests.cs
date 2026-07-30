using Mocksmith.Core.Generation;

namespace Mocksmith.Tests;

public class DesignPatchTests
{
    private const string SampleHtml =
        """
        <!doctype html><html><head><style>:root { --color-bg: #111; }</style></head>
        <body><h1>Hello</h1></body></html>
        """;

    [Fact]
    public void ToCss_RendersTokensAndRules()
    {
        var patch = new DesignPatch();
        patch.SetToken("--color-bg", "#222");
        patch.SetRule("h1", "font-size", "3rem");
        patch.SetRule(".hero", "color", "#fff");

        var css = patch.ToCss();

        Assert.Contains(":root { --color-bg: #222; }", css);
        Assert.Contains("h1 { font-size: 3rem; }", css);
        Assert.Contains(".hero { color: #fff; }", css);
    }

    [Fact]
    public void Bake_InsertsBeforeHeadClose()
    {
        var baked = DesignPatch.Bake(SampleHtml, "h1 { color: red; }");

        Assert.Contains("<style id=\"mocksmith-patch\">", baked);
        Assert.True(
            baked.IndexOf("mocksmith-patch", StringComparison.Ordinal) < baked.IndexOf("</head>", StringComparison.Ordinal));
    }

    [Fact]
    public void Bake_IsIdempotent_ReplacingExistingBlock()
    {
        var once = DesignPatch.Bake(SampleHtml, "h1 { color: red; }");
        var twice = DesignPatch.Bake(once, "h1 { color: blue; }");

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(twice, "mocksmith-patch"));
        Assert.Contains("blue", twice);
        Assert.DoesNotContain("red", twice);
    }

    [Fact]
    public void Bake_EmptyCss_RemovesExistingBlock()
    {
        var once = DesignPatch.Bake(SampleHtml, "h1 { color: red; }");
        var removed = DesignPatch.Bake(once, "");

        Assert.DoesNotContain("mocksmith-patch", removed);
    }

    [Fact]
    public void ExtractExistingCss_RoundTrips()
    {
        var baked = DesignPatch.Bake(SampleHtml, "h1 { color: red; }");

        Assert.Equal("h1 { color: red; }", DesignPatch.ExtractExistingCss(baked));
        Assert.Null(DesignPatch.ExtractExistingCss(SampleHtml));
    }

    [Fact]
    public void Values_AreSanitizedAgainstBreakout()
    {
        var patch = new DesignPatch();
        patch.SetRule("h1", "color", "red } body { display: none");
        patch.SetToken("--color-bg", "</style><script>alert(1)</script>");

        var css = patch.ToCss();

        Assert.DoesNotContain("{ display", css);
        Assert.DoesNotContain("<script", css);
        Assert.DoesNotContain("</style>", css);
    }

    [Theory]
    [InlineData("h1; body")]
    [InlineData("h1 { }")]
    [InlineData("</style>")]
    public void InvalidSelectors_AreRejected(string selector)
    {
        var patch = new DesignPatch();

        Assert.Throws<ArgumentException>(() => patch.SetRule(selector, "color", "red"));
    }

    [Theory]
    [InlineData("color;background")]
    [InlineData("color:red")]
    [InlineData("--not-a-property")]
    public void InvalidProperties_AreRejected(string property)
    {
        var patch = new DesignPatch();

        Assert.Throws<ArgumentException>(() => patch.SetRule("h1", property, "red"));
    }

    [Fact]
    public void InvalidTokenNames_AreRejected()
    {
        var patch = new DesignPatch();

        Assert.Throws<ArgumentException>(() => patch.SetToken("color-bg", "#fff"));
        Assert.Throws<ArgumentException>(() => patch.SetToken("--bad token", "#fff"));
    }
}
