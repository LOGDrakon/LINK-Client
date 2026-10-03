using Link.Core.Framing;
using Link.Core.Security;

namespace Link.Core.Transport;

/// <summary>
/// Transport capable de parler LINK v2 (format binaire et chiffrement de session).
/// Implémenté par <see cref="LinkByteTransportBase"/> (série, TCP, BLE, Android…).
/// </summary>
public interface ILinkSecureTransport : ILinkTransport
{
    /// <summary>Format d'émission. Les deux formats sont toujours acceptés en réception.</summary>
    LinkWireFormat WireFormat { get; set; }

    /// <summary>Chiffrement de session actif (null : trames en clair).</summary>
    ILinkFrameProtector? Protector { get; set; }
}
