using Link.Client.Models;
using Link.Core.Frames;

namespace Link.Client.Helpers;

public sealed class GetVHelper
{
    private readonly LinkClient _client;

    public GetVHelper(LinkClient client)
    {
        _client = client;
    }

    public async Task<LinkDeviceInfo> ExecuteAsync(string appId)
    {
        var frame = await _client.SendCommandAsync(appId, "GETV");

        if (!frame.IsReturn || frame.ReturnedCommand != "GETV")
            throw new InvalidOperationException("Invalid GETV response");

        return LinkDeviceInfo.Parse(appId, frame.ReturnArguments);
    }
}
