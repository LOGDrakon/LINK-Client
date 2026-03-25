using System.Security.Cryptography;
using System.Text;
using Link.Client.Hashing;

namespace Link.Client.Tests;

public class LinkHashProviderFactoryTests
{
    [Theory]
    [InlineData("SHA1")]
    [InlineData("SHA256")]
    [InlineData("SHA384")]
    [InlineData("SHA512")]
    [InlineData("sha256")]
    [InlineData("Sha512")]
    public void Create_SupportedAlgorithm_ReturnsProvider(string algorithm)
    {
        var provider = LinkHashProviderFactory.Create(algorithm);
        Assert.NotNull(provider);
    }

    [Theory]
    [InlineData("MD5")]
    [InlineData("UNKNOWN")]
    [InlineData("")]
    public void Create_UnsupportedAlgorithm_ReturnsNull(string algorithm)
    {
        var provider = LinkHashProviderFactory.Create(algorithm);
        Assert.Null(provider);
    }

    [Fact]
    public void ComputeHash_SHA256_ReturnsCorrectHex()
    {
        var provider = LinkHashProviderFactory.Create("SHA256")!;
        var input = "client_noncedevice_noncepassword";
        var expected = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(input)))
            .ToLowerInvariant();

        Assert.Equal(expected, provider.ComputeHash(input));
    }

    [Fact]
    public void ComputeHash_SHA512_ReturnsCorrectHex()
    {
        var provider = LinkHashProviderFactory.Create("SHA512")!;
        var input = "nonce1nonce2secret";
        var expected = Convert.ToHexString(
            SHA512.HashData(Encoding.UTF8.GetBytes(input)))
            .ToLowerInvariant();

        Assert.Equal(expected, provider.ComputeHash(input));
    }

    [Fact]
    public void ComputeHash_SHA1_ReturnsCorrectHex()
    {
        var provider = LinkHashProviderFactory.Create("SHA1")!;
        var input = "test";
        var expected = Convert.ToHexString(
            SHA1.HashData(Encoding.UTF8.GetBytes(input)))
            .ToLowerInvariant();

        Assert.Equal(expected, provider.ComputeHash(input));
    }
}
