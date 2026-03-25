using Link.Client.Helpers;
using Link.Client.Models;

namespace Link.Client.Extensions;

public static class ChangePasswordExtensions
{
    public static Task<LinkChangePasswordResult> ChangePasswordAsync(
        this LinkClient client,
        string appId,
        string oldPassword,
        string newPassword,
        LinkDeviceInfo deviceInfo,
        LinkAuthNonces nonces,
        CancellationToken ct = default)
    {
        var helper = new ChangePasswordHelper(client);
        return helper.ExecuteAsync(appId, oldPassword, newPassword, deviceInfo, nonces, ct);
    }
}
