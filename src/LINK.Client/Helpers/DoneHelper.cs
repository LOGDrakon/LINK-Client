using Link.Core.Commands;

namespace Link.Client.Helpers;

public sealed class DoneHelper
{
    private readonly LinkClient _client;

    public DoneHelper(LinkClient client)
    {
        _client = client;
    }

    public async Task ExecuteAsync(string appId, CancellationToken ct = default)
    {
        var frame = await _client.SendCommandAsync(appId, LinkCommand.Done, ct)
            .ConfigureAwait(false);

        if (!frame.IsReturn || frame.ReturnedCommand != LinkCommand.Done)
            throw new InvalidOperationException("Invalid DONE response");

        var result = frame.ReturnArguments.FirstOrDefault();

        if (result != "OK")
            throw new InvalidOperationException($"DONE failed: {result ?? "no response"}");
    }
}
