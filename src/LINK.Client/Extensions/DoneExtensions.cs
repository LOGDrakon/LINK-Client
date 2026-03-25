using Link.Client.Helpers;

namespace Link.Client.Extensions;

public static class DoneExtensions
{
    public static Task DoneAsync(
        this LinkClient client,
        string appId,
        CancellationToken ct = default)
    {
        var helper = new DoneHelper(client);
        return helper.ExecuteAsync(appId, ct);
    }
}
