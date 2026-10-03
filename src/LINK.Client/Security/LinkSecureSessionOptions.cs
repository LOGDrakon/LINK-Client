namespace Link.Client.Security;

public enum LinkTrustPolicy
{
    /// <summary>Épingle l'empreinte au premier contact puis exige la même (recommandé).</summary>
    TrustOnFirstUse,

    /// <summary>Exige <see cref="LinkSecureSessionOptions.ExpectedFingerprint"/> ou une empreinte déjà épinglée.</summary>
    Strict,

    /// <summary>Accepte toute identité (tests uniquement : pas de protection contre le MITM).</summary>
    AcceptAny,
}

public sealed class LinkSecureSessionOptions
{
    /// <summary>Empreinte attendue (étiquette, QR code…). Prioritaire sur le magasin.</summary>
    public string? ExpectedFingerprint { get; init; }

    /// <summary>Magasin d'épinglage. Null : aucune mémorisation entre deux sessions.</summary>
    public ILinkTrustStore? TrustStore { get; init; }

    /// <summary>Clé d'identification du device dans le magasin (défaut : APP-ID + UID lu par GETV).</summary>
    public string? DeviceKey { get; init; }

    public LinkTrustPolicy TrustPolicy { get; init; } = LinkTrustPolicy.TrustOnFirstUse;

    /// <summary>Délai de la poignée de main (ECC sur MCU : plusieurs centaines de ms).</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Itérations PBKDF2 maximales acceptées du device (protège le client d'un déni de service).</summary>
    public int MaxKdfIterations { get; init; } = 5_000_000;
}
