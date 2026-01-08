namespace Link.Client.Models;

public sealed record LinkSecurityState
{
    public bool IsAuthenticated { get; init; }
    public bool IsLocked { get; init; }
    public string EncryptionMode { get; init; } = "NONE";

    public static LinkSecurityState Locked(string encryptionMode = "NONE")
        => new() { IsAuthenticated = false, IsLocked = true, EncryptionMode = encryptionMode };

    public static LinkSecurityState Unlocked(string encryptionMode = "NONE")
        => new() { IsAuthenticated = true, IsLocked = false, EncryptionMode = encryptionMode };
}
