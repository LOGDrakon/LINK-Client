namespace Link.Client.Crypto;

public interface ILinkCryptoProvider
{
    string Mode { get; }          // NONE, AES128, ...
    bool IsEnabled { get; }

    byte[] Encrypt(ReadOnlySpan<byte> data);
    byte[] Decrypt(ReadOnlySpan<byte> data);
}
