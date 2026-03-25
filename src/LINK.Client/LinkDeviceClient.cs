using Link.Client.Extensions;
using Link.Client.Helpers;
using Link.Client.Models;
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

    public Task<LinkAuthResult> AuthenticateAsync(
        string password,
        LinkDeviceInfo deviceInfo,
        LinkAuthNonces? existingNonces = null,
        CancellationToken ct = default)
    {
        var helper = new AuthHelper(_client);
        return helper.ExecuteAsync(AppId, password, deviceInfo, existingNonces, ct);
    }

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

    public Task DoneAsync(CancellationToken ct = default)
        => _client.DoneAsync(AppId, ct);

    public Task<LinkFrame> SendAsync(string command, CancellationToken ct = default, params string[] args)
        => _client.SendCommandAsync(AppId, command, ct, args);
}