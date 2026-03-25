using System.Security.Cryptography;

namespace Link.Client.Hashing;

public static class LinkHashProviderFactory
{
    public static ILinkHashProvider? Create(string algorithm)
    {
        return algorithm.ToUpperInvariant() switch
        {
            "SHA1" => new LinkHashProvider("SHA1", SHA1.HashData),
            "SHA256" => new LinkHashProvider("SHA256", SHA256.HashData),
            "SHA384" => new LinkHashProvider("SHA384", SHA384.HashData),
            "SHA512" => new LinkHashProvider("SHA512", SHA512.HashData),
            _ => null
        };
    }
}
