using Link.Client.Helpers;
using Link.Client.Models;

namespace Link.Client.Extensions;

public static class AuthExtensions
{
    public static Task<LinkAuthResult> AuthenticateAsync(
        this LinkClient client,
        string appId,
        string password,
        LinkDeviceInfo deviceInfo,
        LinkAuthNonces? existingNonces = null)
    {
        var helper = new AuthHelper(client);
        return helper.ExecuteAsync(appId, password, deviceInfo, existingNonces);
    }
}
