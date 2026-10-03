using System.Net;
using System.Text;
using Link.Client.Extensions;
using Link.Client.Tests.Fakes;
using Link.Core.Framing;
using Link.Transport.Ble;
using Link.Transport.Tcp;

namespace Link.Client.Tests;

public class TransportV2Tests
{
    private sealed class FakeGattConnection : ILinkGattConnection
    {
        public FakeV2Device? Device { get; set; }
        public List<int> WriteSizes { get; } = new();
        public bool IsConnected { get; private set; }
        public int MaxWriteSize => 20; // MTU 23 par défaut
        public LinkBleProfile? Profile { get; private set; }

        public event Action<byte[]>? NotificationReceived;
        public event Action? Disconnected;

        public Task ConnectAsync(LinkBleProfile profile, CancellationToken ct = default)
        {
            Profile = profile;
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken ct = default)
        {
            IsConnected = false;
            Disconnected?.Invoke();
            return Task.CompletedTask;
        }

        public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            WriteSizes.Add(data.Length);
            Device?.Input(data.ToArray());
            return Task.CompletedTask;
        }

        // Le device notifie aussi par paquets de 20 octets
        public void Notify(byte[] data)
        {
            foreach (var chunk in data.Chunk(20))
                NotificationReceived?.Invoke(chunk);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Ble_SecureSession_ChunksToMtu()
    {
        var gatt = new FakeGattConnection();
        gatt.Device = new FakeV2Device(gatt.Notify);
        var transport = new LinkBleTransport(gatt, LinkBleProfile.NordicUart);
        await using var client = new LinkClient(new LinkClientOptions { Transport = transport });
        await client.ConnectAsync();

        Assert.Equal(LinkBleProfile.NordicUart, gatt.Profile);
        Assert.Equal(LinkWireFormat.V2Binary, transport.WireFormat);

        await client.OpenSecureSessionAsync("DRAGON");
        var pong = await client.SendCommandAsync("DRAGON", "PING");

        Assert.Equal("PONG", pong.ReturnArguments[0]);
        Assert.True(pong.IsEncrypted);
        Assert.All(gatt.WriteSizes, size => Assert.True(size <= 20));
        Assert.Contains(gatt.WriteSizes, size => size == 20); // HELLO (> 260 o) a bien été découpé
    }

    [Fact]
    public void LanDiscovery_ParsesDeviceResponse()
    {
        var datagram = Encoding.ASCII.GetBytes(
            "LINK\u001fDRAGON\u001fRETURN\u001fDISCOVER\u001fUID=ABC\u001fMODEL=Dragon\u001fNAME=Salon" +
            "\u001fPORT=5005\u001fPROTO=1,2\u001fFP=0011223344556677\0");

        var device = LinkLanDiscovery.ParseResponse(datagram, IPAddress.Parse("192.168.1.42"));

        Assert.NotNull(device);
        Assert.Equal("DRAGON", device!.AppId);
        Assert.Equal(5005, device.Port);
        Assert.Equal("Salon", device.Name);
        Assert.Equal("0011223344556677", device.Fingerprint);
        Assert.Equal(new[] { 1, 2 }, device.Protocols);
        Assert.Equal("192.168.1.42", device.ToTcpOptions().Host);
    }

    [Fact]
    public void LanDiscovery_IgnoresUnrelatedDatagrams()
        => Assert.Null(LinkLanDiscovery.ParseResponse(Encoding.ASCII.GetBytes("hello"), IPAddress.Loopback));
}
