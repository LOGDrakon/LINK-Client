using System.Text;
using Link.Core.Frames;
using Link.Core.Framing;

namespace Link.Core.Parsing;

/// <summary>
/// Analyseur texte LINK v1 (conservé pour compatibilité).
/// Pour un flux d'octets pouvant contenir des trames v2, utilisez <see cref="LinkStreamDecoder"/>.
/// </summary>
public sealed class LinkParser
{
    private readonly StringBuilder _buffer = new();
    private bool _overflow;

    /// <summary>Taille maximale d'une trame ; au-delà, la trame est ignorée.</summary>
    public int MaxFrameSize { get; init; } = LinkFrameCodec.DefaultMaxFrameSize;

    public event Action<LinkFrame>? FrameReceived;

    public void Feed(ReadOnlySpan<char> data)
    {
        foreach (char c in data)
        {
            if (c == '\0')
            {
                if (!_overflow)
                    TryParseFrame(_buffer.ToString());
                _buffer.Clear();
                _overflow = false;
            }
            else if (_buffer.Length < MaxFrameSize)
            {
                _buffer.Append(c);
            }
            else
            {
                _overflow = true;
            }
        }
    }

    private void TryParseFrame(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;

        var frame = LinkFrameCodec.DecodeV1(LinkFrameCodec.V1Encoding.GetBytes(raw));
        if (frame is not null)
            FrameReceived?.Invoke(frame);
    }
}
