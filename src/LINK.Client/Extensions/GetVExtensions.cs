using Link.Client.Helpers;
using Link.Client.Models;

namespace Link.Client.Extensions;

public static class GetVExtensions
{
    public static Task<LinkDeviceInfo> GetDeviceInfoAsync(
        this LinkClient client,
        string appId,
        CancellationToken ct = default)
    {
        var helper = new GetVHelper(client);
        return helper.ExecuteAsync(appId, ct);
    }
}
