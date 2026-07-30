using Mocksmith.Core.Security;

namespace Mocksmith.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_ThenVerify_Succeeds()
    {
        var encoded = PasswordHasher.Hash("correct horse battery staple");

        Assert.True(PasswordHasher.Verify("correct horse battery staple", encoded));
    }

    [Fact]
    public void Verify_WrongPassword_Fails()
    {
        var encoded = PasswordHasher.Hash("right-password");

        Assert.False(PasswordHasher.Verify("wrong-password", encoded));
    }

    [Fact]
    public void Hash_ProducesExpectedFormat()
    {
        var encoded = PasswordHasher.Hash("anything");

        var parts = encoded.Split('.');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.True(int.Parse(parts[1]) >= 100_000);
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentSalts()
    {
        var first = PasswordHasher.Hash("same");
        var second = PasswordHasher.Hash("same");

        Assert.NotEqual(first, second);
        Assert.True(PasswordHasher.Verify("same", first));
        Assert.True(PasswordHasher.Verify("same", second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256.abc.!!.!!")]
    [InlineData("md5.1000.c2FsdA==.aGFzaA==")]
    public void Verify_MalformedEncodedHash_FailsInsteadOfThrowing(string encoded)
    {
        Assert.False(PasswordHasher.Verify("anything", encoded));
    }
}
