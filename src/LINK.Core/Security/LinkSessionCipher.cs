using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Link.Core.Security;

/// <summary>
/// AES-128-GCM avec compteur explicite et anti-rejeu (LINK v2 §3.4).
/// Corps chiffré = counter (4 o LE) || ciphertext || tag (16 o).
/// </summary>
public sealed class LinkSessionCipher : ILinkFrameProtector, IDisposable
{
    public const int TagSize = 16;
    public const int CounterSize = 4;

    private readonly AesGcm _tx;
    private readonly AesGcm _rx;
    private readonly byte[] _ivTx;
    private readonly byte[] _ivRx;
    private readonly object _lock = new();
    private uint _ctrTx;
    private uint _ctrRx;

    public int Overhead => CounterSize + TagSize;

    public LinkSessionCipher(ReadOnlySpan<byte> keyTx, ReadOnlySpan<byte> keyRx,
                             ReadOnlySpan<byte> ivTx, ReadOnlySpan<byte> ivRx)
    {
        if (keyTx.Length != 16 || keyRx.Length != 16 || ivTx.Length != 12 || ivRx.Length != 12)
            throw new ArgumentException("Clés AES-128 (16 o) et IV (12 o) attendus.");

        _tx = new AesGcm(keyTx, TagSize);
        _rx = new AesGcm(keyRx, TagSize);
        _ivTx = ivTx.ToArray();
        _ivRx = ivRx.ToArray();
    }

    public byte[] Protect(ReadOnlySpan<byte> header, ReadOnlySpan<byte> plaintext)
    {
        lock (_lock)
        {
            if (_ctrTx == uint.MaxValue)
                throw new InvalidOperationException("Compteur de session épuisé : renégociez la session (HELLO).");

            uint ctr = ++_ctrTx;
            var output = new byte[CounterSize + plaintext.Length + TagSize];
            BinaryPrimitives.WriteUInt32LittleEndian(output, ctr);

            Span<byte> aad = stackalloc byte[header.Length + CounterSize];
            header.CopyTo(aad);
            output.AsSpan(0, CounterSize).CopyTo(aad[header.Length..]);

            _tx.Encrypt(Nonce(_ivTx, ctr), plaintext,
                        output.AsSpan(CounterSize, plaintext.Length),
                        output.AsSpan(CounterSize + plaintext.Length, TagSize),
                        aad);
            return output;
        }
    }

    public byte[]? Unprotect(ReadOnlySpan<byte> header, ReadOnlySpan<byte> body)
    {
        if (body.Length < Overhead)
            return null;

        lock (_lock)
        {
            uint ctr = BinaryPrimitives.ReadUInt32LittleEndian(body);
            if (ctr <= _ctrRx)
                return null; // rejeu

            int ctLen = body.Length - Overhead;
            var plaintext = new byte[ctLen];

            Span<byte> aad = stackalloc byte[header.Length + CounterSize];
            header.CopyTo(aad);
            body[..CounterSize].CopyTo(aad[header.Length..]);

            try
            {
                _rx.Decrypt(Nonce(_ivRx, ctr), body.Slice(CounterSize, ctLen),
                            body.Slice(CounterSize + ctLen, TagSize), plaintext, aad);
            }
            catch (CryptographicException)
            {
                return null;
            }

            _ctrRx = ctr;
            return plaintext;
        }
    }

    private static byte[] Nonce(byte[] iv, uint ctr)
    {
        var nonce = (byte[])iv.Clone();
        nonce[8] ^= (byte)(ctr >> 24);
        nonce[9] ^= (byte)(ctr >> 16);
        nonce[10] ^= (byte)(ctr >> 8);
        nonce[11] ^= (byte)ctr;
        return nonce;
    }

    public void Dispose()
    {
        _tx.Dispose();
        _rx.Dispose();
        CryptographicOperations.ZeroMemory(_ivTx);
        CryptographicOperations.ZeroMemory(_ivRx);
    }
}
