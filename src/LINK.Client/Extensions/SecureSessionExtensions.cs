using Link.Client.Helpers;
using Link.Client.Models;
using Link.Client.Security;

namespace Link.Client.Extensions;

public static class SecureSessionExtensions
{
    /// <summary>Ouvre une session chiffrée LINK v2 (HELLO) et bascule le transport en format binaire.</summary>
    public static Task<LinkSecureSession> OpenSecureSessionAsync(
        this LinkClient client, string appId, LinkSecureSessionOptions? options = null, CancellationToken ct = default)
        => new SecureSessionHelper(client).OpenAsync(appId, options, ct);

    /// <summary>Authentification v2 (preuve PBKDF2/HMAC, envoyée chiffrée).</summary>
    public static Task<LinkSecureAuthResult> AuthenticateSecureAsync(
        this LinkClient client, string password, CancellationToken ct = default)
        => new SecureSessionHelper(client).AuthenticateAsync(password, ct);

    /// <summary>Définit le mot de passe d'un device neuf (non provisionné).</summary>
    public static Task SetPasswordAsync(
        this LinkClient client, string password, int iterations = SecureSessionHelper.DefaultKdfIterations,
        CancellationToken ct = default)
        => new SecureSessionHelper(client).SetPasswordAsync(password, iterations, ct);

    /// <summary>Change le mot de passe (session authentifiée).</summary>
    public static Task ChangePasswordSecureAsync(
        this LinkClient client, string oldPassword, string newPassword,
        int iterations = SecureSessionHelper.DefaultKdfIterations, CancellationToken ct = default)
        => new SecureSessionHelper(client).ChangePasswordAsync(oldPassword, newPassword, iterations, ct);

    public static Task FactoryResetAsync(this LinkClient client, bool newIdentity = false, CancellationToken ct = default)
        => new SecureSessionHelper(client).FactoryResetAsync(newIdentity, ct);

    /// <summary>Termine la session (DONE) et efface les clés.</summary>
    public static Task CloseSecureSessionAsync(this LinkClient client, CancellationToken ct = default)
        => new SecureSessionHelper(client).CloseAsync(ct);
}
