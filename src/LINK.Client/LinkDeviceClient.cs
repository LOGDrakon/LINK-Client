using Link.Client.Extensions;
using Link.Client.Helpers;
using Link.Client.Models;
using Link.Client.Security;
using Link.Core.Frames;

namespace Link.Client;

public sealed class LinkDeviceClient
{
    private readonly LinkClient _client;

    public string AppId { get; }

    public LinkDeviceClient(LinkClient client, string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException(nameof(appId));

        _client = client ?? throw new ArgumentNullException(nameof(client));
        AppId = appId;
    }

    public Task<LinkDeviceInfo> GetDeviceInfoAsync(CancellationToken ct = default)
        => _client.GetDeviceInfoAsync(AppId, ct);

    /// <summary>Authentification LINK v1 (legacy, mot de passe stocké en clair côté device).</summary>
    public Task<LinkAuthResult> AuthenticateAsync(
        string password,
        LinkDeviceInfo deviceInfo,
        LinkAuthNonces? existingNonces = null,
        CancellationToken ct = default)
    {
        var helper = new AuthHelper(_client);
        return helper.ExecuteAsync(AppId, password, deviceInfo, existingNonces, ct);
    }

    /// <summary>Changement de mot de passe LINK v1 (legacy).</summary>
    public Task<LinkChangePasswordResult> ChangePasswordAsync(
        string oldPassword,
        string newPassword,
        LinkDeviceInfo deviceInfo,
        LinkAuthNonces nonces,
        CancellationToken ct = default)
    {
        var helper = new ChangePasswordHelper(_client);
        return helper.ExecuteAsync(AppId, oldPassword, newPassword, deviceInfo, nonces, ct);
    }

    // ---- LINK v2 ----

    public Task<LinkSecureSession> OpenSecureSessionAsync(LinkSecureSessionOptions? options = null, CancellationToken ct = default)
        => _client.OpenSecureSessionAsync(AppId, options, ct);

    public Task<LinkSecureAuthResult> AuthenticateSecureAsync(string password, CancellationToken ct = default)
        => _client.AuthenticateSecureAsync(password, ct);

    public Task SetPasswordAsync(string password, CancellationToken ct = default)
        => _client.SetPasswordAsync(password, ct: ct);

    public Task ChangePasswordSecureAsync(string oldPassword, string newPassword, CancellationToken ct = default)
        => _client.ChangePasswordSecureAsync(oldPassword, newPassword, ct: ct);

    public Task CloseSecureSessionAsync(CancellationToken ct = default)
        => _client.CloseSecureSessionAsync(ct);

    public Task DoneAsync(CancellationToken ct = default)
        => _client.DoneAsync(AppId, ct);

    public Task<LinkFrame> SendAsync(string command, CancellationToken ct = default, params string[] args)
        => _client.SendCommandAsync(AppId, command, ct, args);
}
