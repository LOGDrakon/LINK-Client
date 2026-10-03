using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Link.Core.Frames;
using Link.Core.Framing;
using Link.Core.Security;

namespace Link.Client.Tests.Fakes;

/// <summary>Device LINK v2 minimal en C# (miroir de LINK-Device/link_core.c) pour les tests.</summary>
internal sealed class FakeV2Device
{
    private readonly LinkStreamDecoder _decoder = new();
    private readonly Action<byte[]> _send;
    private LinkSessionCipher? _cipher;
    private byte[] _th = Array.Empty<byte>();
    private bool _authenticated;

    public string AppId { get; } = "DRAGON";
    public ECDsa Identity { get; set; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public byte[] Salt { get; private set; } = RandomNumberGenerator.GetBytes(16);
    public int Iterations { get; private set; } = 10_000;
    public byte[]? Verifier { get; private set; }
    public bool CorruptSignature { get; set; }
    public int AuthFailures { get; private set; }

    public FakeV2Device(Action<byte[]> send)
    {
        _send = send;
        _decoder.FrameReceived += Handle;
    }

    public byte[] IdentityPublicKey
    {
        get
        {
            var p = Identity.ExportParameters(false);
            return [0x04, .. p.Q.X!, .. p.Q.Y!];
        }
    }

    public string Fingerprint => Convert.ToHexString(SHA256.HashData(IdentityPublicKey)[..8]).ToLowerInvariant();

    public void Input(byte[] data) => _decoder.Feed(data);

    public void SendEvent(string name, params string[] args)
    {
        var frame = new LinkFrame(AppId, name, args) { Sequence = 0 };
        _send(LinkFrameCodec.EncodeV2(frame, LinkPacketFlags.Event, _cipher));
    }

    private void Reply(LinkFrame req, params string[] args)
    {
        var frame = new LinkFrame(AppId, "RETURN", [req.Command, .. args]) { Sequence = req.Sequence };
        _send(req.Version == 2
            ? LinkFrameCodec.EncodeV2(frame, LinkPacketFlags.Response, req.IsEncrypted ? _cipher : null)
            : LinkFrameCodec.EncodeV1(frame));
    }

    private void Handle(LinkFrame f)
    {
        if (f.AppId != AppId)
            return;
        bool secure = _cipher is not null;
        if (secure && !f.IsEncrypted && f.Command != "HELLO")
        {
            Reply(f, "ERR", "NO_SESSION");
            return;
        }

        switch (f.Command)
        {
            case "GETV":
                Reply(f, "LINKv2.0", "UID=TEST-1", "PROTO=1,2", $"FP={Fingerprint}",
                      $"PROV={(Verifier is null ? 0 : 1)}", $"LOCKED={(Verifier is not null && !_authenticated ? "true" : "false")}");
                break;
            case "PING":
                Reply(f, "PONG");
                break;
            case "HELLO":
                Hello(f);
                break;
            case "AUTH":
                if (Verifier is null) { Reply(f, "ERR", "NOT_PROVISIONED"); break; }
                if (Convert.FromHexString(f.Arguments[0]).SequenceEqual(Proof(Verifier, "LINKv2/AUTH")))
                {
                    _authenticated = true;
                    AuthFailures = 0;
                    Reply(f, "OK");
                }
                else
                {
                    AuthFailures++;
                    Reply(f, "ERR", "BAD_PWD", Math.Max(0, 3 - AuthFailures).ToString());
                }
                break;
            case "SETPWD":
                if (Verifier is not null) { Reply(f, "ERR", "ALREADY_PROVISIONED"); break; }
                Salt = Convert.FromHexString(f.Arguments[0]);
                Iterations = int.Parse(f.Arguments[1]);
                Verifier = Convert.FromHexString(f.Arguments[2]);
                _authenticated = true;
                Reply(f, "OK");
                break;
            case "CHPWD":
                if (!_authenticated) { Reply(f, "ERR", "NOT_AUTH"); break; }
                if (!Convert.FromHexString(f.Arguments[0]).SequenceEqual(Proof(Verifier!, "LINKv2/CHPWD")))
                { Reply(f, "ERR", "BAD_OLD_PWD"); break; }
                Salt = Convert.FromHexString(f.Arguments[1]);
                Iterations = int.Parse(f.Arguments[2]);
                Verifier = Convert.FromHexString(f.Arguments[3]);
                Reply(f, "OK");
                break;
            case "SETLED":
                if (Verifier is null || _authenticated)
                    Reply(f, "OK");
                else
                    Reply(f, "ERR", "NOT_AUTH");
                break;
            case "DONE":
                Reply(f, "OK");
                _cipher = null;
                _decoder.Protector = null;
                _authenticated = false;
                break;
            default:
                Reply(f, "ERR", "UNKNOWN_COMMAND");
                break;
        }
    }

    private byte[] Proof(byte[] key, string label)
        => HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(label).Concat(_th).ToArray());

    private void Hello(LinkFrame f)
    {
        var cePub = Convert.FromHexString(f.Arguments[1]);
        var cn = Convert.FromHexString(f.Arguments[2]);
        using var eph = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var ep = eph.ExportParameters(false);
        byte[] dePub = [0x04, .. ep.Q.X!, .. ep.Q.Y!];
        var dn = RandomNumberGenerator.GetBytes(16);
        var dsPub = IdentityPublicKey;
        bool prov = Verifier is not null;

        using var ms = new MemoryStream();
        ms.Write("LINKv2/HS"u8);
        ms.WriteByte((byte)AppId.Length);
        ms.Write(Encoding.UTF8.GetBytes(AppId));
        ms.Write(cePub); ms.Write(cn); ms.Write(dePub); ms.Write(dn); ms.Write(dsPub);
        ms.WriteByte(13);
        ms.Write("PBKDF2-SHA256"u8);
        ms.Write(Salt);
        var it = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(it, (uint)Iterations);
        ms.Write(it);
        ms.WriteByte(prov ? (byte)1 : (byte)0);
        var th = SHA256.HashData(ms.ToArray());
        var sig = Identity.SignHash(th);
        if (CorruptSignature)
            sig[5] ^= 1;

        using var peer = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = cePub[1..33], Y = cePub[33..65] },
        });
        var z = eph.DeriveRawSecretAgreement(peer.PublicKey);
        var okm = HKDF.DeriveKey(HashAlgorithmName.SHA256, z, 56, th, "LINKv2 session keys"u8.ToArray());

        Reply(f, "2", Hex(dePub), Hex(dn), Hex(dsPub), "PBKDF2-SHA256", Hex(Salt),
              Iterations.ToString(), prov ? "1" : "0", Hex(sig));

        _th = th;
        _authenticated = false;
        _cipher = new LinkSessionCipher(okm[16..32], okm[0..16], okm[44..56], okm[32..44]);
        _decoder.Protector = _cipher;
    }

    private static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();
}
