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

    [Fact]
    public void Verify_EmptyHashSegment_FailsClosed()
    {
        // A trailing dot decodes to an empty expected hash; zero-length PBKDF2
        // output would compare equal to it, so this must be rejected outright.
        var parts = PasswordHasher.Hash("pw").Split('.');
        var malformed = $"{parts[0]}.{parts[1]}.{parts[2]}.";

        Assert.False(PasswordHasher.Verify("pw", malformed));
        Assert.False(PasswordHasher.Verify("anything-else", malformed));
    }

    [Fact]
    public void Verify_WrongSaltOrHashLength_Fails()
    {
        var parts = PasswordHasher.Hash("pw").Split('.');
        var shortSalt = Convert.ToBase64String(new byte[4]);
        var shortHash = Convert.ToBase64String(new byte[8]);

        Assert.False(PasswordHasher.Verify("pw", $"{parts[0]}.{parts[1]}.{shortSalt}.{parts[3]}"));
        Assert.False(PasswordHasher.Verify("pw", $"{parts[0]}.{parts[1]}.{parts[2]}.{shortHash}"));
    }
}
