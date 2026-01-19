using Link.Client.Models;
using Link.Core.Commands;

namespace Link.Client.Helpers;

public sealed class GetVHelper
{
    private readonly LinkClient _client;

    public GetVHelper(LinkClient client)
    {
        _client = client;
    }

    public async Task<LinkDeviceInfo> ExecuteAsync(string appId, CancellationToken ct = default)
    {
        var frame = await _client.SendCommandAsync(appId, LinkCommand.GetVersion, ct)
            .ConfigureAwait(false);

        if (!frame.IsReturn || frame.ReturnedCommand != LinkCommand.GetVersion)
            throw new InvalidOperationException("Invalid GETV response");

        return LinkDeviceInfo.Parse(appId, frame.ReturnArguments);
    }
}
