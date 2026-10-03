using System.Security.Cryptography;
using Link.Core.Security;

namespace Link.Client.Security;

/// <summary>État d'une session sécurisée LINK v2 établie par <c>HELLO</c>.</summary>
public sealed class LinkSecureSession : IDisposable
{
    internal LinkSecureSession(string appId, byte[] transcriptHash, LinkSessionCipher cipher,
                               string fingerprint, byte[] salt, int iterations, bool isProvisioned,
                               bool fingerprintWasPinned)
    {
        AppId = appId;
        TranscriptHash = transcriptHash;
        Cipher = cipher;
        Fingerprint = fingerprint;
        Salt = salt;
        Iterations = iterations;
        IsProvisioned = isProvisioned;
        IsFingerprintPinned = fingerprintWasPinned;
    }

    public string AppId { get; }

    /// <summary>Empreinte de la clé d'identité du device (16 hex).</summary>
    public string Fingerprint { get; }

    /// <summary>L'empreinte correspondait à une valeur connue (épinglée ou attendue).</summary>
    public bool IsFingerprintPinned { get; }

    public bool IsProvisioned { get; internal set; }

    public bool IsAuthenticated { get; internal set; }

    internal byte[] TranscriptHash { get; }
    internal byte[] Salt { get; }
    internal int Iterations { get; }
    internal LinkSessionCipher Cipher { get; }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(TranscriptHash);
        Cipher.Dispose();
    }
}
