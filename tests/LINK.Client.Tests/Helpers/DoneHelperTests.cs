using Link.Client.Extensions;
using Link.Client.Tests.Fakes;
using Link.Core.Frames;

namespace Link.Client.Tests.Helpers;

public class DoneHelperTests
{
    private static (LinkClient Client, FakeTransport Transport) CreateClient()
    {
        var transport = new FakeTransport();
        var client = new LinkClient(new LinkClientOptions { Transport = transport });
        client.ConnectAsync().GetAwaiter().GetResult();
        return (client, transport);
    }

    [Fact]
    public async Task Done_OK_CompletesSuccessfully()
    {
        var (client, transport) = CreateClient();

        transport.FrameSent += frame =>
        {
            if (frame.Command == "DONE")
                transport.SimulateReceive(new LinkFrame("APP", "RETURN", "DONE", "OK"));
        };

        await client.DoneAsync("APP");

        Assert.Contains(transport.SentFrames, f => f.Command == "DONE");
    }

    [Fact]
    public async Task Done_ERR_ThrowsInvalidOperation()
    {
        var (client, transport) = CreateClient();

        transport.FrameSent += frame =>
        {
            if (frame.Command == "DONE")
                transport.SimulateReceive(new LinkFrame("APP", "RETURN", "DONE", "ERR"));
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.DoneAsync("APP"));
    }

    [Fact]
    public async Task Done_InvalidResponse_ThrowsTimeout()
    {
        var (client, transport) = CreateClient();

        transport.FrameSent += frame =>
        {
            if (frame.Command == "DONE")
                transport.SimulateReceive(new LinkFrame("APP", "RETURN", "GETV", "OK"));
        };

        await Assert.ThrowsAsync<TimeoutException>(
            () => client.DoneAsync("APP"));
    }
}
