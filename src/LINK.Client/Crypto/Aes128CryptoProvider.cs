namespace Link.Client.Crypto;

public sealed class Aes128CryptoProvider : ILinkCryptoProvider
{
    private readonly byte[] _key;

    public string Mode => "AES128";
    public bool IsEnabled => true;

    public Aes128CryptoProvider(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 16)
            throw new ArgumentException("AES-128 key must be 16 bytes.", nameof(key));
        _key = key;
    }

    public byte[] Encrypt(ReadOnlySpan<byte> data)
        => throw new NotImplementedException("AES-128 encryption not yet implemented.");

    public byte[] Decrypt(ReadOnlySpan<byte> data)
        => throw new NotImplementedException("AES-128 decryption not yet implemented.");
}
