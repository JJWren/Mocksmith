using Mocksmith.Core.Services;

namespace Mocksmith.Tests;

public class TagQueryTests
{
    private static bool Eval(string query, params string[] tags)
        => TagQuery.Parse(query).Matches(tags);

    [Fact]
    public void SingleTag_MatchesPresence()
    {
        Assert.True(Eval("dark", "dark", "dashboard"));
        Assert.False(Eval("dark", "light"));
    }

    [Fact]
    public void And_RequiresBoth()
    {
        Assert.True(Eval("dark AND dashboard", "dark", "dashboard"));
        Assert.False(Eval("dark AND dashboard", "dark"));
    }

    [Fact]
    public void Or_RequiresEither()
    {
        Assert.True(Eval("dark OR light", "light"));
        Assert.False(Eval("dark OR light", "retro"));
    }

    [Fact]
    public void AndBindsTighterThanOr()
    {
        // a OR b AND c  ==  a OR (b AND c)
        Assert.True(Eval("retro OR dark AND dashboard", "retro"));
        Assert.True(Eval("retro OR dark AND dashboard", "dark", "dashboard"));
        Assert.False(Eval("retro OR dark AND dashboard", "dark"));
    }

    [Fact]
    public void Parentheses_OverridePrecedence()
    {
        // (a OR b) AND c
        Assert.False(Eval("(retro OR dark) AND dashboard", "retro"));
        Assert.True(Eval("(retro OR dark) AND dashboard", "retro", "dashboard"));
    }

    [Fact]
    public void KeywordsAndTags_AreCaseInsensitiveAndNormalized()
    {
        Assert.True(Eval("Dark_Mode and DASHBOARD", "dark-mode", "dashboard"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("AND dark")]
    [InlineData("dark AND")]
    [InlineData("dark OR OR light")]
    [InlineData("(dark")]
    [InlineData("dark)")]
    [InlineData("!!!")]
    public void InvalidQueries_ThrowFormatException(string query)
    {
        Assert.Throws<FormatException>(() => TagQuery.Parse(query));
        Assert.False(TagQuery.TryParse(query, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_ValidQuery_ReturnsTrue()
    {
        Assert.True(TagQuery.TryParse("dark AND (a OR b)", out var error));
        Assert.Null(error);
    }
}
