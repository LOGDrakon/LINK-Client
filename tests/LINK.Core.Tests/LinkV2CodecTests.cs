using System.Security.Cryptography;
using Link.Core.Frames;
using Link.Core.Framing;
using Link.Core.Security;

public class LinkV2CodecTests
{
    [Fact]
    public void Crc16_MatchesCcittFalseVector()
        => Assert.Equal(0x29B1, LinkCrc16.Compute("123456789"u8));

    [Fact]
    public void Cobs_MatchesSpecVector()
    {
        var encoded = LinkCobs.Encode(new byte[] { 0x11, 0x22, 0x00, 0x33 });
        Assert.Equal(new byte[] { 0x03, 0x11, 0x22, 0x02, 0x33 }, encoded);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x00, 0x33 }, LinkCobs.Decode(encoded));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(253)]
    [InlineData(254)]
    [InlineData(255)]
    [InlineData(1000)]
    public void Cobs_RoundTrip_HasNoZero(int size)
    {
        var data = new byte[size];
        new Random(size).NextBytes(data);
        for (int i = 0; i < size; i += 7)
            data[i] = 0;
        var encoded = LinkCobs.Encode(data);
        Assert.DoesNotContain((byte)0, encoded);
        Assert.Equal(data, LinkCobs.Decode(encoded));

        var noZero = Enumerable.Repeat((byte)0xAA, size).ToArray();
        Assert.Equal(noZero, LinkCobs.Decode(LinkCobs.Encode(noZero)));
    }

    [Fact]
    public void Body_MatchesSpecVector()
    {
        var body = LinkFrameCodec.EncodeBody(new LinkFrame("DRAGON", "PING"));
        Assert.Equal(Convert.FromHexString("06445241474F4E0450494E47"), body);
    }

    [Fact]
    public void V2_RoundTrip_PreservesSequenceAndBinarySafeArguments()
    {
        var frame = new LinkFrame("DRAGON", "ECHO", "a\u001fb", "", "°C", new string('x', 300)) { Sequence = 513 };
        var wire = LinkFrameCodec.EncodeV2(frame);

        Assert.Equal(0, wire[^1]);
        Assert.DoesNotContain((byte)0, wire[..^1]);

        var decoded = LinkFrameCodec.DecodeV2(wire.AsSpan(0, wire.Length - 1), null);
        Assert.NotNull(decoded);
        Assert.Equal(2, decoded!.Version);
        Assert.Equal(513, decoded.Sequence);
        Assert.Equal(frame.Arguments, decoded.Arguments);
    }

    [Fact]
    public void V2_CorruptedCrc_IsRejected()
    {
        var wire = LinkFrameCodec.EncodeV2(new LinkFrame("APP", "PING") { Sequence = 1 });
        var packet = LinkCobs.Decode(wire.AsSpan(0, wire.Length - 1))!;
        packet[6] ^= 1;
        Assert.Null(LinkFrameCodec.DecodeV2(LinkCobs.Encode(packet), null));
    }

    private static (LinkSessionCipher A, LinkSessionCipher B) CipherPair()
    {
        var k1 = RandomNumberGenerator.GetBytes(16);
        var k2 = RandomNumberGenerator.GetBytes(16);
        var iv1 = RandomNumberGenerator.GetBytes(12);
        var iv2 = RandomNumberGenerator.GetBytes(12);
        return (new LinkSessionCipher(k1, k2, iv1, iv2), new LinkSessionCipher(k2, k1, iv2, iv1));
    }

    [Fact]
    public void Encrypted_RoundTrip_ReplayAndTamperRejected()
    {
        var (client, device) = CipherPair();
        var wire = LinkFrameCodec.EncodeV2(new LinkFrame("APP", "AUTH", "secret-proof") { Sequence = 7 },
                                           LinkPacketFlags.None, client);
        var block = wire.AsSpan(0, wire.Length - 1).ToArray();

        // Le texte clair n'apparaît pas sur le fil
        Assert.DoesNotContain("secret-proof", System.Text.Encoding.Latin1.GetString(LinkCobs.Decode(block)!));

        // Sans clé : rejeté
        Assert.Null(LinkFrameCodec.DecodeV2(block, null));

        var decoded = LinkFrameCodec.DecodeV2(block, device);
        Assert.NotNull(decoded);
        Assert.True(decoded!.IsEncrypted);
        Assert.Equal("secret-proof", decoded.Arguments[0]);

        // Rejeu
        Assert.Null(LinkFrameCodec.DecodeV2(block, device));

        // Altération (CRC recalculé : seul le tag GCM peut détecter)
        var wire2 = LinkFrameCodec.EncodeV2(new LinkFrame("APP", "PING") { Sequence = 8 }, LinkPacketFlags.None, client);
        var packet = LinkCobs.Decode(wire2.AsSpan(0, wire2.Length - 1))!;
        packet[12] ^= 0x40;
        var crc = LinkCrc16.Compute(packet.AsSpan(0, packet.Length - 2));
        packet[^2] = (byte)crc;
        packet[^1] = (byte)(crc >> 8);
        Assert.Null(LinkFrameCodec.DecodeV2(LinkCobs.Encode(packet), device));
    }

    [Fact]
    public void StreamDecoder_HandlesMixedV1V2_ChunksAndOverflow()
    {
        var decoder = new LinkStreamDecoder { MaxFrameSize = 256 };
        var frames = new List<LinkFrame>();
        decoder.FrameReceived += frames.Add;

        var stream = new List<byte>();
        stream.AddRange(LinkFrameCodec.EncodeV1(new LinkFrame("APP", "RETURN", "GETV", "LINKv1.1")));
        stream.AddRange(Enumerable.Repeat((byte)'A', 2000)); // trame géante sans délimiteur
        stream.Add(0);
        stream.AddRange(LinkFrameCodec.EncodeV2(new LinkFrame("APP", "RETURN", "PING", "PONG") { Sequence = 3 }));

        foreach (var chunk in stream.Chunk(5))
            decoder.Feed(chunk);

        Assert.Equal(2, frames.Count);
        Assert.Equal(1, frames[0].Version);
        Assert.Equal("LINKv1.1", frames[0].ReturnArguments[0]);
        Assert.Equal(2, frames[1].Version);
        Assert.Equal(3, frames[1].Sequence);
    }

    [Fact]
    public void V1_EmptyArgumentsArePreserved()
    {
        var frame = LinkFrameCodec.DecodeV1("LINK\u001fAPP\u001fCMD\u001f\u001fX"u8);
        Assert.NotNull(frame);
        Assert.Equal(new[] { "", "X" }, frame!.Arguments);
    }
}
