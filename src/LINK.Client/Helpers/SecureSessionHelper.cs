using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Link.Client.Extensions;
using Link.Client.Models;
using Link.Client.Security;
using Link.Core.Commands;
using Link.Core.Framing;
using Link.Core.Frames;
using Link.Core.Security;
using Link.Core.Transport;

namespace Link.Client.Helpers;

/// <summary>
/// Session sécurisée LINK v2 : HELLO (ECDH P-256 + signature ECDSA du device),
/// AUTH / SETPWD / CHPWD par vérificateur PBKDF2-SHA256, FRESET, DONE.
/// Voir docs/LINK_Protocol_v2.md §4.
/// </summary>
public sealed class SecureSessionHelper
{
    public const string KdfName = "PBKDF2-SHA256";
    public const int DefaultKdfIterations = 100_000;
    private const int NonceSize = 16;
    private const int SaltSize = 16;

    private static readonly byte[] HandshakeLabel = "LINKv2/HS"u8.ToArray();
    private static readonly byte[] AuthLabel = "LINKv2/AUTH"u8.ToArray();
    private static readonly byte[] ChangePasswordLabel = "LINKv2/CHPWD"u8.ToArray();
    private static readonly byte[] KeysInfo = "LINKv2 session keys"u8.ToArray();

    private readonly LinkClient _client;

    public SecureSessionHelper(LinkClient client)
    {
        _client = client;
    }

    private ILinkSecureTransport SecureTransport =>
        _client.Transport as ILinkSecureTransport
        ?? throw new NotSupportedException(
            "Le transport ne supporte pas LINK v2 (dérivez de LinkByteTransportBase).");

    // ------------------------------------------------------------------ HELLO

    public async Task<LinkSecureSession> OpenAsync(string appId, LinkSecureSessionOptions? options = null,
                                                   CancellationToken ct = default)
    {
        options ??= new LinkSecureSessionOptions();
        var transport = SecureTransport;

        string? deviceKey = options.DeviceKey;
        if (deviceKey is null && options.TrustStore is not null)
        {
            var info = await _client.GetDeviceInfoAsync(appId, ct).ConfigureAwait(false);
            deviceKey = $"{appId}/{info.Uid ?? "?"}";
        }

        // La poignée de main part en clair, au format v2.
        _client.Session?.Dispose();
        _client.Session = null;
        transport.Protector = null;
        transport.WireFormat = LinkWireFormat.V2Binary;

        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var cePub = ExportPublicKey(ecdh.ExportParameters(false));
        var cn = RandomNumberGenerator.GetBytes(NonceSize);

        var frame = await _client.SendCommandAsync(appId, LinkCommand.Hello, options.HandshakeTimeout, ct,
                                                   "2", Hex(cePub), Hex(cn)).ConfigureAwait(false);
        var r = ExpectReturn(frame, LinkCommand.Hello);
        if (r.Count < 9 || r[0] != "2")
            throw new LinkSecurityException("Réponse HELLO invalide.");

        var dePub = FromHex(r[1], 65);
        var dn = FromHex(r[2], NonceSize);
        var dsPub = FromHex(r[3], 65);
        var kdf = r[4];
        var salt = FromHex(r[5], SaltSize);
        if (!int.TryParse(r[6], out var iterations) || iterations < 1 || iterations > options.MaxKdfIterations)
            throw new LinkSecurityException($"Nombre d'itérations PBKDF2 refusé : {r[6]}");
        bool provisioned = r[7] == "1";
        var sig = FromHex(r[8], 64);

        if (kdf != KdfName)
            throw new LinkSecurityException($"KDF non supportée : {kdf}");

        var th = TranscriptHash(appId, cePub, cn, dePub, dn, dsPub, kdf, salt, (uint)iterations, provisioned);

        using (var verifier = ImportEcdsa(dsPub))
        {
            if (!verifier.VerifyHash(th, sig))
                throw new LinkSecurityException("Signature HELLO invalide : identité du device non prouvée.");
        }

        var fingerprint = Fingerprint(dsPub);
        bool pinned = CheckTrust(options, deviceKey, fingerprint);

        byte[] okm;
        using (var peer = ECDiffieHellman.Create(new ECParameters
               {
                   Curve = ECCurve.NamedCurves.nistP256,
                   Q = new ECPoint { X = dePub[1..33], Y = dePub[33..65] },
               }))
        {
            var z = ecdh.DeriveRawSecretAgreement(peer.PublicKey);
            okm = HKDF.DeriveKey(HashAlgorithmName.SHA256, z, 56, th, KeysInfo);
            CryptographicOperations.ZeroMemory(z);
        }

        var cipher = new LinkSessionCipher(okm.AsSpan(0, 16), okm.AsSpan(16, 16),
                                           okm.AsSpan(32, 12), okm.AsSpan(44, 12));
        CryptographicOperations.ZeroMemory(okm);

        var session = new LinkSecureSession(appId, th, cipher, fingerprint, salt, iterations, provisioned, pinned);
        transport.Protector = cipher;
        _client.Session = session;
        return session;
    }

    // ------------------------------------------------------------------ AUTH

    public async Task<LinkSecureAuthResult> AuthenticateAsync(string password, CancellationToken ct = default)
    {
        var session = RequireSession();
        ArgumentException.ThrowIfNullOrEmpty(password);

        var key = DeriveKey(password, session.Salt, session.Iterations);
        var proof = Proof(key, AuthLabel, session.TranscriptHash);
        CryptographicOperations.ZeroMemory(key);

        var frame = await _client.SendCommandAsync(session.AppId, LinkCommand.Auth, ct, Hex(proof))
            .ConfigureAwait(false);
        var r = ReturnArgs(frame, LinkCommand.Auth);

        if (r.FirstOrDefault() == "OK")
        {
            session.IsAuthenticated = true;
            return new LinkSecureAuthResult { Success = true };
        }

        var code = r.Count > 1 ? r[1] : "ERR";
        var result = new LinkSecureAuthResult { Success = false, Error = code };
        if (code == LinkSecureAuthResult.ErrorBadPassword && r.Count > 2 && int.TryParse(r[2], out var left))
            result = result with { RemainingAttempts = left };
        if (code == LinkSecureAuthResult.ErrorLocked && r.Count > 2 && int.TryParse(r[2], out var secs))
            result = result with { RetryAfter = TimeSpan.FromSeconds(secs) };
        return result;
    }

    // ------------------------------------------------------------------ SETPWD / CHPWD

    public async Task SetPasswordAsync(string password, int iterations = DefaultKdfIterations, CancellationToken ct = default)
    {
        var session = RequireSession();
        var (salt, key) = NewVerifier(password, iterations);

        var frame = await _client.SendCommandAsync(session.AppId, LinkCommand.SetPassword, ct,
                                                   Hex(salt), iterations.ToString(), Hex(key)).ConfigureAwait(false);
        CryptographicOperations.ZeroMemory(key);
        EnsureOk(frame, LinkCommand.SetPassword);
        session.IsProvisioned = true;
        session.IsAuthenticated = true;
    }

    public async Task ChangePasswordAsync(string oldPassword, string newPassword,
                                          int iterations = DefaultKdfIterations, CancellationToken ct = default)
    {
        var session = RequireSession();
        ArgumentException.ThrowIfNullOrEmpty(oldPassword);

        var oldKey = DeriveKey(oldPassword, session.Salt, session.Iterations);
        var oldProof = Proof(oldKey, ChangePasswordLabel, session.TranscriptHash);
        CryptographicOperations.ZeroMemory(oldKey);
        var (salt, key) = NewVerifier(newPassword, iterations);

        var frame = await _client.SendCommandAsync(session.AppId, LinkCommand.ChangePassword, ct,
                                                   Hex(oldProof), Hex(salt), iterations.ToString(), Hex(key))
            .ConfigureAwait(false);
        CryptographicOperations.ZeroMemory(key);
        EnsureOk(frame, LinkCommand.ChangePassword);
    }

    // ------------------------------------------------------------------ FRESET / DONE

    public async Task FactoryResetAsync(bool newIdentity = false, CancellationToken ct = default)
    {
        var session = RequireSession();
        var frame = newIdentity
            ? await _client.SendCommandAsync(session.AppId, LinkCommand.FactoryReset, ct, "IDENTITY").ConfigureAwait(false)
            : await _client.SendCommandAsync(session.AppId, LinkCommand.FactoryReset, ct).ConfigureAwait(false);
        EnsureOk(frame, LinkCommand.FactoryReset);
        DropSession();
    }

    public async Task CloseAsync(CancellationToken ct = default)
    {
        var session = _client.Session;
        if (session is null)
            return;
        try
        {
            var frame = await _client.SendCommandAsync(session.AppId, LinkCommand.Done, ct).ConfigureAwait(false);
            EnsureOk(frame, LinkCommand.Done);
        }
        finally
        {
            DropSession();
        }
    }

    private void DropSession()
    {
        if (_client.Transport is ILinkSecureTransport t)
            t.Protector = null;
        _client.Session?.Dispose();
        _client.Session = null;
    }

    // ------------------------------------------------------------------ primitives

    public static byte[] DeriveKey(string password, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);

    public static string Fingerprint(byte[] devicePublicKey)
        => Convert.ToHexString(SHA256.HashData(devicePublicKey).AsSpan(0, 8)).ToLowerInvariant();

    internal static byte[] TranscriptHash(string appId, byte[] cePub, byte[] cn, byte[] dePub, byte[] dn,
                                          byte[] dsPub, string kdf, byte[] salt, uint iterations, bool provisioned)
    {
        var app = Encoding.UTF8.GetBytes(appId);
        if (app.Length > 64)
            app = app[..64];
        var kdfBytes = Encoding.ASCII.GetBytes(kdf);

        using var ms = new MemoryStream();
        ms.Write(HandshakeLabel);
        ms.WriteByte((byte)app.Length);
        ms.Write(app);
        ms.Write(cePub);
        ms.Write(cn);
        ms.Write(dePub);
        ms.Write(dn);
        ms.Write(dsPub);
        ms.WriteByte((byte)kdfBytes.Length);
        ms.Write(kdfBytes);
        ms.Write(salt);
        Span<byte> it = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(it, iterations);
        ms.Write(it);
        ms.WriteByte(provisioned ? (byte)1 : (byte)0);
        return SHA256.HashData(ms.ToArray());
    }

    private static byte[] Proof(byte[] key, byte[] label, byte[] th)
        => HMACSHA256.HashData(key, label.Concat(th).ToArray());

    private static (byte[] Salt, byte[] Key) NewVerifier(string password, int iterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (iterations < 10_000)
            throw new ArgumentOutOfRangeException(nameof(iterations), "10 000 itérations minimum.");
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return (salt, DeriveKey(password, salt, iterations));
    }

    private bool CheckTrust(LinkSecureSessionOptions options, string? deviceKey, string fingerprint)
    {
        if (options.ExpectedFingerprint is { } expected)
        {
            if (!string.Equals(expected, fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new LinkSecurityException(
                    $"Empreinte du device inattendue ({fingerprint}, attendu {expected}) : possible attaque MITM.");
            if (options.TrustStore is not null && deviceKey is not null)
                options.TrustStore.SetFingerprint(deviceKey, fingerprint);
            return true;
        }

        var known = deviceKey is null ? null : options.TrustStore?.GetFingerprint(deviceKey);
        if (known is not null)
        {
            if (!string.Equals(known, fingerprint, StringComparison.OrdinalIgnoreCase) &&
                options.TrustPolicy != LinkTrustPolicy.AcceptAny)
                throw new LinkSecurityException(
                    $"L'identité du device '{deviceKey}' a changé ({known} -> {fingerprint}). " +
                    "Reset usine ou attaque MITM : retirez l'empreinte du magasin si le changement est légitime.");
            return true;
        }

        if (options.TrustPolicy == LinkTrustPolicy.Strict)
            throw new LinkSecurityException($"Device inconnu (empreinte {fingerprint}) et politique stricte.");

        if (options.TrustPolicy == LinkTrustPolicy.TrustOnFirstUse && deviceKey is not null)
            options.TrustStore?.SetFingerprint(deviceKey, fingerprint);
        return false;
    }

    private LinkSecureSession RequireSession()
        => _client.Session ?? throw new InvalidOperationException("Aucune session sécurisée : appelez OpenSecureSessionAsync.");

    private static IReadOnlyList<string> ReturnArgs(LinkFrame frame, string command)
    {
        if (!frame.IsReturn || frame.ReturnedCommand != command)
            throw new InvalidOperationException($"Réponse {command} invalide");
        return frame.ReturnArguments;
    }

    private static IReadOnlyList<string> ExpectReturn(LinkFrame frame, string command)
    {
        var r = ReturnArgs(frame, command);
        if (r.FirstOrDefault() == "ERR")
            throw new LinkDeviceErrorException(command, r.Count > 1 ? r[1] : "ERR", r.Skip(2).ToArray());
        return r;
    }

    private static void EnsureOk(LinkFrame frame, string command)
    {
        var r = ExpectReturn(frame, command);
        if (r.FirstOrDefault() != "OK")
            throw new LinkDeviceErrorException(command, r.FirstOrDefault() ?? "no response", Array.Empty<string>());
    }

    private static byte[] ExportPublicKey(ECParameters p)
    {
        var pub = new byte[65];
        pub[0] = 0x04;
        p.Q.X!.CopyTo(pub.AsSpan(1 + 32 - p.Q.X!.Length));
        p.Q.Y!.CopyTo(pub.AsSpan(33 + 32 - p.Q.Y!.Length));
        return pub;
    }

    private static ECDsa ImportEcdsa(byte[] pub)
    {
        if (pub.Length != 65 || pub[0] != 0x04)
            throw new LinkSecurityException("Clé publique du device invalide.");
        return ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = pub[1..33], Y = pub[33..65] },
        });
    }

    private static string Hex(byte[] data) => Convert.ToHexString(data).ToLowerInvariant();

    private static byte[] FromHex(string s, int expectedLength)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(s);
        }
        catch (FormatException)
        {
            throw new LinkSecurityException("Champ hexadécimal invalide dans HELLO.");
        }
        if (bytes.Length != expectedLength)
            throw new LinkSecurityException("Taille de champ invalide dans HELLO.");
        return bytes;
    }
}
