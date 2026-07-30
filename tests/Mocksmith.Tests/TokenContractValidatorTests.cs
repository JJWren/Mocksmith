using Mocksmith.Core.Generation;

namespace Mocksmith.Tests;

public class TokenContractValidatorTests
{
    private const string ValidSample =
        """
        <!doctype html><html><head><style>
        :root { --color-bg: #111; --color-text: #eee; --font-heading: Georgia, serif; --font-body: system-ui; }
        body { background: var(--color-bg); }
        </style></head><body>
        <h1>Hi</h1>
        <a href="https://example.com">external nav link is fine</a>
        <img src="data:image/svg+xml;base64,abc" alt="" />
        <script type="application/json" id="mocksmith-tokens">
        {"tokens":[{"name":"--color-bg","label":"Background","category":"color"}]}
        </script>
        </body></html>
        """;

    [Fact]
    public void ValidSample_PassesClean()
    {
        Assert.Empty(TokenContractValidator.Validate(ValidSample));
    }

    [Fact]
    public void MissingManifest_IsViolation()
    {
        var html = ValidSample.Replace("mocksmith-tokens", "something-else");

        Assert.Contains(TokenContractValidator.Validate(html), v => v.Code == "missing-manifest");
    }

    [Fact]
    public void InvalidManifestJson_IsViolation()
    {
        var html = ValidSample.Replace("""{"tokens":[{"name":"--color-bg","label":"Background","category":"color"}]}""", "{not json");

        Assert.Contains(TokenContractValidator.Validate(html), v => v.Code == "invalid-manifest");
    }

    [Fact]
    public void MissingRootTokens_IsViolation()
    {
        var violations = TokenContractValidator.Validate("<html><body>plain</body></html>");

        Assert.Contains(violations, v => v.Code == "missing-root");
    }

    [Theory]
    [InlineData("""<script src="https://cdn.example.com/lib.js"></script>""")]
    [InlineData("""<link rel="stylesheet" href="//fonts.example.com/x.css">""")]
    [InlineData("""<img src="https://example.com/pic.png">""")]
    [InlineData("""<style>.x{background:url(https://example.com/bg.png)}</style>""")]
    [InlineData("""<style>@import "https://example.com/theme.css";</style>""")]
    public void ExternalResources_AreViolations(string fragment)
    {
        var html = ValidSample.Replace("<h1>Hi</h1>", fragment);

        Assert.Contains(TokenContractValidator.Validate(html), v => v.Code == "external-request");
    }

    [Fact]
    public void AnchorLinksAndDataUris_AreNotViolations()
    {
        Assert.DoesNotContain(TokenContractValidator.Validate(ValidSample), v => v.Code == "external-request");
    }

    [Fact]
    public void ManifestExtraction_ReturnsRawJson()
    {
        var json = TokenContractValidator.TryExtractManifestJson(ValidSample);

        Assert.NotNull(json);
        Assert.Contains("--color-bg", json);
        Assert.Null(TokenContractValidator.TryExtractManifestJson("<html></html>"));
    }
}
