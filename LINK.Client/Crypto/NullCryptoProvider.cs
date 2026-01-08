namespace Link.Client.Crypto;

public sealed class NullCryptoProvider : ILinkCryptoProvider
{
    public string Mode => "NONE";
    public bool IsEnabled => false;

    public byte[] Encrypt(ReadOnlySpan<byte> data)
        => data.ToArray();

    public byte[] Decrypt(ReadOnlySpan<byte> data)
        => data.ToArray();
}
