using System.Text;
using Link.Core.Frames;

namespace Link.Core.Parsing;

public sealed class LinkParser
{
    private readonly StringBuilder _buffer = new();

    public event Action<LinkFrame>? FrameReceived;

    public void Feed(ReadOnlySpan<char> data)
    {
        foreach (char c in data)
        {
            if (c == '\0')
            {
                TryParseFrame(_buffer.ToString());
                _buffer.Clear();
            }
            else
            {
                _buffer.Append(c);
            }
        }
    }

    private void TryParseFrame(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;

        var parts = raw.Split('\x1f', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return;

        if (parts[0] != "LINK")
            return;

        // Cas spécial GETAPP
        if (parts.Length == 2 && parts[1] == "GETAPP")
        {
            FrameReceived?.Invoke(new LinkFrame(null, "GETAPP"));
            return;
        }

        // Cas standard : LINK\x1fAPP\x1fCOMMAND[\x1fARGS...]
        if (parts.Length < 3)
            return;

        string appId = parts[1];
        string command = parts[2];
        string[] args = parts.Length > 3
            ? parts[3..]
            : Array.Empty<string>();

        FrameReceived?.Invoke(new LinkFrame(appId, command, args));
    }
}
