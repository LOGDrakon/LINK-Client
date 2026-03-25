namespace Link.Client.Models;

public sealed record LinkChangePasswordResult
{
    public const string ErrorBadOldPassword = "BAD_OLD_PWD";
    public const string ErrorBadCrc = "BAD_CRC";

    public bool Success { get; init; }
    public string? Error { get; init; }
}
