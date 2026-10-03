namespace Link.Client.Models;

/// <summary>Résultat d'une authentification LINK v2.</summary>
public sealed record LinkSecureAuthResult
{
    public const string ErrorBadPassword = "BAD_PWD";
    public const string ErrorLocked = "LOCKED";
    public const string ErrorNotProvisioned = "NOT_PROVISIONED";

    public bool Success { get; init; }
    public string? Error { get; init; }

    /// <summary>Essais restants avant temporisation (BAD_PWD).</summary>
    public int? RemainingAttempts { get; init; }

    /// <summary>Délai avant nouvel essai (LOCKED).</summary>
    public TimeSpan? RetryAfter { get; init; }
}
