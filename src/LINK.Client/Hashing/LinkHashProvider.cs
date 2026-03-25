using System.Text;

namespace Link.Client.Hashing;

public sealed class LinkHashProvider : ILinkHashProvider
{
    private readonly Func<byte[], byte[]> _hashFunc;

    public string Algorithm { get; }

    internal LinkHashProvider(string algorithm, Func<byte[], byte[]> hashFunc)
    {
        Algorithm = algorithm;
        _hashFunc = hashFunc;
    }

    public string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = _hashFunc(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
