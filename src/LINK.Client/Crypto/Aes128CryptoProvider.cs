using System.Security.Cryptography;

namespace Link.Client.Crypto;

/// <summary>
/// Chiffrement AES-128-GCM d'un bloc applicatif avec une clé pré-partagée
/// (sortie : nonce 12 o || ciphertext || tag 16 o).
/// Pour sécuriser le lien avec le device, préférez la session LINK v2
/// (<c>OpenSecureSessionAsync</c>) qui négocie des clés éphémères.
/// </summary>
public sealed class Aes128CryptoProvider : ILinkCryptoProvider, IDisposable
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly AesGcm _aes;

    public string Mode => "AES128";
    public bool IsEnabled => true;

    public Aes128CryptoProvider(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 16)
            throw new ArgumentException("AES-128 key must be 16 bytes.", nameof(key));
        _aes = new AesGcm(key, TagSize);
    }

    public byte[] Encrypt(ReadOnlySpan<byte> data)
    {
        var output = new byte[NonceSize + data.Length + TagSize];
        RandomNumberGenerator.Fill(output.AsSpan(0, NonceSize));
        _aes.Encrypt(output.AsSpan(0, NonceSize), data,
                     output.AsSpan(NonceSize, data.Length),
                     output.AsSpan(NonceSize + data.Length, TagSize));
        return output;
    }

    public byte[] Decrypt(ReadOnlySpan<byte> data)
    {
        if (data.Length < NonceSize + TagSize)
            throw new CryptographicException("Données chiffrées trop courtes.");
        int len = data.Length - NonceSize - TagSize;
        var output = new byte[len];
        _aes.Decrypt(data[..NonceSize], data.Slice(NonceSize, len), data.Slice(NonceSize + len, TagSize), output);
        return output;
    }

    public void Dispose() => _aes.Dispose();
}
