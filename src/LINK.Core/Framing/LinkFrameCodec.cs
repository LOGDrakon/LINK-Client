using System.Buffers.Binary;
using System.Text;
using Link.Core.Frames;
using Link.Core.Security;

namespace Link.Core.Framing;

/// <summary>Sérialisation des trames LINK v1 (texte) et v2 (binaire COBS).</summary>
public static class LinkFrameCodec
{
    public const byte V2Magic = 0xB2;
    public const int V2HeaderSize = 6;
    public const int V2CrcSize = 2;
    public const int DefaultMaxFrameSize = 1024;

    /// <summary>Encodage des trames v1 : Latin-1 (préserve les octets 0x80-0xFF, ex. « ° »).</summary>
    public static readonly Encoding V1Encoding = Encoding.Latin1;

    public static byte[] EncodeV1(LinkFrame frame) => V1Encoding.GetBytes(frame.ToString());

    /// <summary>Corps v2 : suite de champs varint(len) || UTF-8 (APP-ID, commande, arguments).</summary>
    public static byte[] EncodeBody(LinkFrame frame)
    {
        using var ms = new MemoryStream();
        WriteField(ms, frame.AppId ?? string.Empty);
        WriteField(ms, frame.Command);
        foreach (var arg in frame.Arguments)
            WriteField(ms, arg);
        return ms.ToArray();
    }

    /// <summary>Paquet v2 complet sur le fil (COBS + délimiteur 0x00).</summary>
    public static byte[] EncodeV2(LinkFrame frame, LinkPacketFlags flags = LinkPacketFlags.None,
                                  ILinkFrameProtector? protector = null)
    {
        var body = EncodeBody(frame);
        if (protector is not null)
            flags |= LinkPacketFlags.Encrypted;
        else
            flags &= ~LinkPacketFlags.Encrypted;

        int bodyLen = body.Length + (protector?.Overhead ?? 0);
        if (bodyLen > ushort.MaxValue)
            throw new ArgumentException("Trame trop longue.");

        var packet = new byte[V2HeaderSize + bodyLen + V2CrcSize];
        packet[0] = V2Magic;
        packet[1] = (byte)flags;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), frame.Sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), (ushort)bodyLen);

        if (protector is not null)
            protector.Protect(packet.AsSpan(0, V2HeaderSize), body).CopyTo(packet.AsSpan(V2HeaderSize));
        else
            body.CopyTo(packet.AsSpan(V2HeaderSize));

        var crc = LinkCrc16.Compute(packet.AsSpan(0, V2HeaderSize + bodyLen));
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(V2HeaderSize + bodyLen), crc);

        var cobs = LinkCobs.Encode(packet);
        var wire = new byte[cobs.Length + 1];
        cobs.CopyTo(wire, 0);
        return wire;
    }

    /// <summary>Décode un bloc v1 (sans le 0x00 final). Les arguments vides sont conservés.</summary>
    public static LinkFrame? DecodeV1(ReadOnlySpan<byte> block)
    {
        var raw = V1Encoding.GetString(block);
        if (raw.EndsWith('\x1f'))
            raw = raw[..^1];

        var parts = raw.Split('\x1f');
        if (parts.Length < 2 || parts[0] != "LINK")
            return null;

        // LINK\x1fGETAPP / LINK\x1fDISCOVER : sans APP-ID
        if (parts.Length == 2)
            return string.IsNullOrWhiteSpace(parts[1]) ? null : new LinkFrame(null, parts[1]);

        if (string.IsNullOrWhiteSpace(parts[2]))
            return null;

        return new LinkFrame(parts[1], parts[2], parts[3..]);
    }

    /// <summary>Décode un bloc v2 (COBS, sans le 0x00 final). Retourne null si invalide.</summary>
    public static LinkFrame? DecodeV2(ReadOnlySpan<byte> block, ILinkFrameProtector? protector, int maxFrameSize = DefaultMaxFrameSize)
    {
        var packet = LinkCobs.Decode(block);
        if (packet is null || packet.Length < V2HeaderSize + V2CrcSize || packet.Length > maxFrameSize || packet[0] != V2Magic)
            return null;

        var flags = (LinkPacketFlags)packet[1];
        var seq = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2));
        int bodyLen = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4));
        if (V2HeaderSize + bodyLen + V2CrcSize != packet.Length)
            return null;

        var crc = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(V2HeaderSize + bodyLen));
        if (crc != LinkCrc16.Compute(packet.AsSpan(0, V2HeaderSize + bodyLen)))
            return null;

        ReadOnlySpan<byte> body = packet.AsSpan(V2HeaderSize, bodyLen);
        bool encrypted = flags.HasFlag(LinkPacketFlags.Encrypted);
        if (encrypted)
        {
            if (protector is null)
                return null;
            var plain = protector.Unprotect(packet.AsSpan(0, V2HeaderSize), body);
            if (plain is null)
                return null;
            body = plain;
        }

        var fields = ReadFields(body);
        if (fields is null || fields.Count < 2 || string.IsNullOrWhiteSpace(fields[1]))
            return null;

        return new LinkFrame(fields[0].Length == 0 ? null : fields[0], fields[1], fields.Skip(2).ToArray())
        {
            Version = 2,
            Sequence = seq,
            IsEncrypted = encrypted,
            IsEvent = flags.HasFlag(LinkPacketFlags.Event),
        };
    }

    private static void WriteField(Stream s, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        uint len = (uint)bytes.Length;
        while (len >= 0x80)
        {
            s.WriteByte((byte)(len | 0x80));
            len >>= 7;
        }
        s.WriteByte((byte)len);
        s.Write(bytes);
    }

    private static List<string>? ReadFields(ReadOnlySpan<byte> body)
    {
        var fields = new List<string>();
        int i = 0;
        while (i < body.Length)
        {
            int len = 0, shift = 0;
            while (true)
            {
                if (i >= body.Length || shift > 21)
                    return null;
                byte b = body[i++];
                len |= (b & 0x7F) << shift;
                shift += 7;
                if ((b & 0x80) == 0)
                    break;
            }
            if (i + len > body.Length)
                return null;
            fields.Add(Encoding.UTF8.GetString(body.Slice(i, len)));
            i += len;
        }
        return fields;
    }
}
