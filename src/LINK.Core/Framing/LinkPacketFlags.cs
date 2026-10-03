namespace Link.Core.Framing;

[Flags]
public enum LinkPacketFlags : byte
{
    None = 0,
    Encrypted = 0x01,
    Response = 0x02,
    Event = 0x04,
}
