using Link.Core.Frames;
using Link.Core.Security;

namespace Link.Core.Framing;

/// <summary>
/// Découpe un flux d'octets (UART, USB, TCP, BLE) en trames LINK v1 ou v2.
/// Les blocs sont délimités par 0x00 ; tout bloc dépassant <see cref="MaxFrameSize"/>
/// est ignoré jusqu'au délimiteur suivant (protection mémoire).
/// </summary>
public sealed class LinkStreamDecoder
{
    private static ReadOnlySpan<byte> V1Prefix => "LINK\x1f"u8;

    private readonly List<byte> _buffer = new();
    private bool _overflow;

    public int MaxFrameSize { get; init; } = LinkFrameCodec.DefaultMaxFrameSize;

    /// <summary>Déchiffrement des paquets v2 chiffrés (null : paquets chiffrés ignorés).</summary>
    public ILinkFrameProtector? Protector { get; set; }

    public event Action<LinkFrame>? FrameReceived;

    /// <summary>Bloc rejeté (CRC, COBS, tag GCM, rejeu, taille…), pour le diagnostic.</summary>
    public event Action<byte[]>? FrameRejected;

    public void Feed(ReadOnlySpan<byte> data)
    {
        // Encodage COBS : jusqu'à 1 octet de surcoût par 254 + 1.
        int maxWire = MaxFrameSize + MaxFrameSize / 254 + 2;

        foreach (var b in data)
        {
            if (b != 0)
            {
                if (_buffer.Count < maxWire)
                    _buffer.Add(b);
                else
                    _overflow = true;
                continue;
            }

            if (!_overflow && _buffer.Count > 0)
                HandleBlock(_buffer.ToArray());

            _buffer.Clear();
            _overflow = false;
        }
    }

    public void Reset()
    {
        _buffer.Clear();
        _overflow = false;
    }

    private void HandleBlock(byte[] block)
    {
        var frame = block.AsSpan().StartsWith(V1Prefix)
            ? LinkFrameCodec.DecodeV1(block)
            : LinkFrameCodec.DecodeV2(block, Protector, MaxFrameSize);

        if (frame is null)
        {
            FrameRejected?.Invoke(block);
            return;
        }

        FrameReceived?.Invoke(frame);
    }
}
