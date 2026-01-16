using Link.Client.Extensions;
using Link.Client.Models;
using Link.Client.Tests.Fakes;
using Link.Core.Frames;

namespace Link.Client.Tests;

public class GetVHelperTests
{
    [Fact]
    public async Task GetV_ParsesDeviceInfoCorrectly()
    {
        var transport = new FakeTransport();
        var client = new LinkClient(new LinkClientOptions
        {
            Transport = transport
        });

        await client.ConnectAsync();

        var task = client.GetDeviceInfoAsync("DRAGON");

        transport.SimulateReceive(new LinkFrame(
            "DRAGON",
            "RETURN",
            "GETV",
            "LINKv1.1",
            "UID=0x1234",
            "MODEL=Dragon",
            "ENC=AES128",
            "LOCKED=true"
        ));

        var info = await task;

        Assert.Equal("DRAGON", info.AppId);
        Assert.Equal("LINKv1.1", info.Version);
        Assert.Equal("0x1234", info.Uid);
        Assert.Equal("Dragon", info.Model);
        Assert.True(info.IsLocked);
        Assert.Equal("AES128", info.EncryptionMode);
    }
}
