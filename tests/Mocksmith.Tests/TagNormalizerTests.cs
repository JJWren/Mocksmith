using Mocksmith.Core.Services;

namespace Mocksmith.Tests;

public class TagNormalizerTests
{
    [Theory]
    [InlineData("Dark Mode", "dark-mode")]
    [InlineData("  brutalist  ", "brutalist")]
    [InlineData("UPPER", "upper")]
    [InlineData("a   b", "a-b")]
    [InlineData("tag_with.mixed/chars", "tag-with-mixed-chars")]
    [InlineData("dashboard", "dashboard")]
    [InlineData("!!!", "")]
    [InlineData("", "")]
    public void Normalize_ProducesKebabCase(string input, string expected)
    {
        Assert.Equal(expected, TagNormalizer.Normalize(input));
    }

    [Fact]
    public void NormalizeSet_DropsEmptiesAndDuplicates_PreservesOrder()
    {
        var result = TagNormalizer.NormalizeSet(["Dark Mode", "dark-mode", "!!!", "Retro", "DARK MODE"]);

        Assert.Equal(["dark-mode", "retro"], result);
    }
}
