using Link.Client.Helpers;
using Link.Client.Models;
using Link.Client.Tests.Fakes;
using Link.Core.Frames;

namespace Link.Client.Tests;

public class LinkClientTests
{
    [Fact]
    public async Task SendCommandAsync_ReturnsResponse()
    {
        var transport = new FakeTransport();
        var client = new LinkClient(new LinkClientOptions
        {
            Transport = transport,
            CommandTimeout = TimeSpan.FromSeconds(1)
        });

        await client.ConnectAsync();

        var task = client.SendCommandAsync("APP", "GETV");

        transport.SimulateReceive(
            new LinkFrame("APP", "RETURN", "GETV", "LINKv1.0"));

        var response = await task;

        Assert.True(response.IsReturn);
        Assert.Equal("GETV", response.ReturnedCommand);
        Assert.Equal("LINKv1.0", response.ReturnArguments[0]);
    }

    [Fact]
    public async Task SendCommandAsync_Timeout_Throws()
    {
        var transport = new FakeTransport();
        var client = new LinkClient(new LinkClientOptions
        {
            Transport = transport,
            CommandTimeout = TimeSpan.FromMilliseconds(100)
        });

        await client.ConnectAsync();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            client.SendCommandAsync("APP", "GETV"));
    }

    [Fact]
    public async Task Return_ForUnknownCommand_IsIgnored()
    {
        var transport = new FakeTransport();
        var client = new LinkClient(new LinkClientOptions
        {
            Transport = transport,
            CommandTimeout = TimeSpan.FromMilliseconds(200)
        });

        await client.ConnectAsync();

        var task = client.SendCommandAsync("APP", "CMD1");

        transport.SimulateReceive(
            new LinkFrame("APP", "RETURN", "CMD2", "OK"));

        await Assert.ThrowsAsync<TimeoutException>(() => task);
    }

    [Fact]
    public async Task SameCommandTwice_Throws()
    {
        var transport = new FakeTransport();
        var client = new LinkClient(new LinkClientOptions
        {
            Transport = transport
        });

        await client.ConnectAsync();

        var t1 = client.SendCommandAsync("APP", "GETV");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SendCommandAsync("APP", "GETV"));
    }

    [Fact]
    public async Task NoEncryption_ReturnsNullProvider()
    {
        var helper = new CryptoNegotiationHelper(null!);

        var info = new LinkDeviceInfo
        {
            EncryptionMode = "NONE",
            IsLocked = false
        };

        var provider = await helper.NegotiateAsync(
            "APP",
            info,
            _ => null);

        Assert.False(provider.IsEnabled);
    }

}
