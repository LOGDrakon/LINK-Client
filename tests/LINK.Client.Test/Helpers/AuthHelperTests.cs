using Link.Client.Extensions;
using Link.Client.Tests.Fakes;
using Link.Core.Frames;

namespace Link.Client.Tests;

public class AuthHelperTests
{
    [Fact]
    public async Task Auth_OK_ReturnsUnlocked()
    {
        var transport = new FakeTransport();
        var client = new LinkClient(new LinkClientOptions
        {
            Transport = transport
        });

        await client.ConnectAsync();

        var task = client.AuthenticateAsync("APP", "1234");

        transport.SimulateReceive(
            new LinkFrame("APP", "RETURN", "AUTH", "OK"));

        var state = await task;

        Assert.True(state.IsAuthenticated);
        Assert.False(state.IsLocked);
    }

    [Fact]
    public async Task Auth_ERR_ReturnsLocked()
    {
        var transport = new FakeTransport();
        var client = new LinkClient(new LinkClientOptions
        {
            Transport = transport
        });

        await client.ConnectAsync();

        var task = client.AuthenticateAsync("APP", "bad");

        transport.SimulateReceive(
            new LinkFrame("APP", "RETURN", "AUTH", "ERR"));

        var state = await task;

        Assert.False(state.IsAuthenticated);
        Assert.True(state.IsLocked);
    }
}
