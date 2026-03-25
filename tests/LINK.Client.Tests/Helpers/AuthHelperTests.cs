using System.Security.Cryptography;
using System.Text;
using Link.Client.Extensions;
using Link.Client.Models;
using Link.Client.Tests.Fakes;
using Link.Core.Frames;

namespace Link.Client.Tests;

public class AuthHelperTests
{
    private static LinkDeviceInfo DeviceWithHash(string hash = "SHA256")
        => new() { HashMethod = hash, IsLocked = true };

    private static (LinkClient Client, FakeTransport Transport) CreateClient()
    {
        var transport = new FakeTransport();
        var client = new LinkClient(new LinkClientOptions { Transport = transport });
        client.ConnectAsync().GetAwaiter().GetResult();
        return (client, transport);
    }

    [Fact]
    public async Task Auth_WithNonceNegotiation_OK_ReturnsUnlockedAndNonces()
    {
        var (client, transport) = CreateClient();
        var deviceInfo = DeviceWithHash();

        transport.FrameSent += frame =>
        {
            if (frame.Command == "AUTH_INIT")
                transport.SimulateReceive(new LinkFrame("APP", "RETURN", "AUTH_INIT", "device_nonce_abc"));
            else if (frame.Command == "AUTH")
                transport.SimulateReceive(new LinkFrame("APP", "RETURN", "AUTH", "OK"));
        };

        var result = await client.AuthenticateAsync("APP", "1234", deviceInfo);

        Assert.True(result.State.IsAuthenticated);
        Assert.False(result.State.IsLocked);
        Assert.NotEmpty(result.Nonces.ClientNonce);
        Assert.Equal("device_nonce_abc", result.Nonces.DeviceNonce);
    }

    [Fact]
    public async Task Auth_WithNonceNegotiation_ERR_ReturnsLocked()
    {
        var (client, transport) = CreateClient();
        var deviceInfo = DeviceWithHash();

        transport.FrameSent += frame =>
        {
            if (frame.Command == "AUTH_INIT")
                transport.SimulateReceive(new LinkFrame("APP", "RETURN", "AUTH_INIT", "device_nonce_xyz"));
            else if (frame.Command == "AUTH")
                transport.SimulateReceive(new LinkFrame("APP", "RETURN", "AUTH", "ERR"));
        };

        var result = await client.AuthenticateAsync("APP", "bad", deviceInfo);

        Assert.False(result.State.IsAuthenticated);
        Assert.True(result.State.IsLocked);
    }

    [Fact]
    public async Task Auth_WithExistingNonces_SkipsAuthInit()
    {
        var (client, transport) = CreateClient();
        var deviceInfo = DeviceWithHash();
        var nonces = new LinkAuthNonces
        {
            ClientNonce = "aabbccdd",
            DeviceNonce = "11223344"
        };

        transport.FrameSent += frame =>
        {
            if (frame.Command == "AUTH")
                transport.SimulateReceive(new LinkFrame("APP", "RETURN", "AUTH", "OK"));
        };

        var result = await client.AuthenticateAsync("APP", "1234", deviceInfo, nonces);

        Assert.True(result.State.IsAuthenticated);
        Assert.Equal("aabbccdd", result.Nonces.ClientNonce);
        Assert.Equal("11223344", result.Nonces.DeviceNonce);

        Assert.DoesNotContain(transport.SentFrames, f => f.Command == "AUTH_INIT");
    }

    [Fact]
    public async Task Auth_WithExistingNonces_SendsCorrectHash()
    {
        var (client, transport) = CreateClient();
        var deviceInfo = DeviceWithHash();
        var nonces = new LinkAuthNonces
        {
            ClientNonce = "client_nonce",
            DeviceNonce = "device_nonce"
        };

        var expectedHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes("client_noncedevice_nonce1234")))
            .ToLowerInvariant();

        transport.FrameSent += frame =>
        {
            if (frame.Command == "AUTH")
                transport.SimulateReceive(new LinkFrame("APP", "RETURN", "AUTH", "OK"));
        };

        await client.AuthenticateAsync("APP", "1234", deviceInfo, nonces);

        var authFrame = transport.SentFrames.Single(f => f.Command == "AUTH");
        Assert.Equal(expectedHash, authFrame.Arguments[0]);
    }

    [Fact]
    public async Task Auth_HashMethodNone_ThrowsInvalidOperation()
    {
        var (client, _) = CreateClient();
        var deviceInfo = new LinkDeviceInfo { HashMethod = "NONE" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.AuthenticateAsync("APP", "1234", deviceInfo));
    }

    [Fact]
    public async Task Auth_UnsupportedHashMethod_ThrowsNotSupported()
    {
        var (client, _) = CreateClient();
        var deviceInfo = new LinkDeviceInfo { HashMethod = "MD5" };

        await Assert.ThrowsAsync<NotSupportedException>(
            () => client.AuthenticateAsync("APP", "1234", deviceInfo));
    }
}
