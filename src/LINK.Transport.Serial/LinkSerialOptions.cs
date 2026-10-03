using System.IO.Ports;
using Link.Core.Framing;

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

    /// <summary>Taille maximale d'une trame reçue (au-delà : ignorée).</summary>
    public int MaxFrameSize { get; init; } = LinkFrameCodec.DefaultMaxFrameSize;

    /// <summary>Format d'émission initial (bascule automatique en v2 avec <c>OpenSecureSessionAsync</c>).</summary>
    public LinkWireFormat WireFormat { get; init; } = LinkWireFormat.V1Text;

    /// <summary>
    /// Lève DTR à l'ouverture. Certains ports USB CDC n'émettent qu'avec DTR levé ;
    /// le port STM32 LINK-Device réinitialise la session quand DTR retombe.
    /// Désactivé par défaut (les pseudo-terminaux / ponts virtuels ne le supportent pas toujours).
    /// </summary>
    public bool DtrEnable { get; init; }

    public bool RtsEnable { get; init; }
}
