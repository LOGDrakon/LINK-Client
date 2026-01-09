using Link.Client.Helpers;
using Link.Client.Models;

namespace Link.Client.Extensions;

public static class AuthExtensions
{
    public static Task<LinkSecurityState> AuthenticateAsync(
        this LinkClient client,
        string appId,
        string password)
    {
        var helper = new AuthHelper(client);
        return helper.ExecuteAsync(appId, password);
    }
}
