namespace Link.Client.Models;

public sealed record LinkCryptoInfo
{
    public string SupportedMode { get; init; } = "NONE";
    public bool IsLocked { get; init; }

    public bool IsEncryptionAvailable => SupportedMode != "NONE";
}
