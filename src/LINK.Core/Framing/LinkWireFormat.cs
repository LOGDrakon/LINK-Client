namespace Link.Core.Framing;

/// <summary>Format utilisé pour émettre les trames sur un transport.</summary>
public enum LinkWireFormat
{
    /// <summary>LINK v1 : texte <c>LINK\x1f…\0</c> (compatible avec tous les devices).</summary>
    V1Text = 1,

    /// <summary>LINK v2 : paquet binaire COBS (séquence, CRC-16, chiffrement possible).</summary>
    V2Binary = 2,
}
