using System.IO.Ports;

namespace Link.Transport.Serial;

public sealed class LinkSerialOptions
{
    public string PortName { get; init; } = string.Empty;
    public int BaudRate { get; init; } = 115200;
    public int DataBits { get; init; } = 8;
    public Parity Parity { get; init; } = Parity.None;
    public StopBits StopBits { get; init; } = StopBits.One;

    /// <summary>
    /// Maximum number of bytes sent per write operation.
    /// Matches the USB FS hardware buffer size on STM32 devices (64 bytes).
    /// Set to 0 to disable chunking.
    /// </summary>
    public int MaxPacketSize { get; init; } = 64;
}
