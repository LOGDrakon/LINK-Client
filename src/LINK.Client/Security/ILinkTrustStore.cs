namespace Link.Client.Security;

/// <summary>
/// Mémorise l'empreinte (clé d'identité) de chaque device pour détecter une usurpation
/// (« Trust On First Use », comme les clés d'hôte SSH).
/// </summary>
public interface ILinkTrustStore
{
    /// <summary>Empreinte épinglée pour ce device, ou null s'il est inconnu.</summary>
    string? GetFingerprint(string deviceKey);

    void SetFingerprint(string deviceKey, string fingerprint);

    void Remove(string deviceKey);
}
