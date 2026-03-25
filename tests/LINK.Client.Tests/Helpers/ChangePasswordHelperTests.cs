using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;
using Link.Client.Extensions;
using Link.Client.Helpers;
using Link.Client.Models;
using Link.Client.Tests.Fakes;
using Link.Core.Frames;

namespace Link.Client.Tests;

public class ChangePasswordHelperTests
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
    public async Task ChangePassword_OK_ReturnsSuccess()
    {
        var (client, transport) = CreateClient();
        var deviceInfo = DeviceWithHash();
        var nonces = new LinkAuthNonces
        {
            ClientNonce = "client_nonce",
            DeviceNonce = "device_nonce"
        };

        transport.FrameSent += frame =>
        {
            if (frame.Command == "CHPWD")
                transport.SimulateReceive(new LinkFrame("APP", "RETURN", "CHPWD", "OK"));
        };

        var result = await client.ChangePasswordAsync("APP", "old_pass", "new_pass", deviceInfo, nonces);

        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ChangePassword_BadOldPassword_ReturnsFailureWithError()
    {
        var (client, transport) = CreateClient();
        var deviceInfo = DeviceWithHash();
        var nonces = new LinkAuthNonces
        {
            ClientNonce = "client_nonce",
            DeviceNonce = "device_nonce"
        };

        transport.FrameSent += frame =>
        {
            if (frame.Command == "CHPWD")
                transport.SimulateReceive(new LinkFrame("APP", "RETURN", "CHPWD", "ERR", "BAD_OLD_PWD"));
        };

        var result = await client.ChangePasswordAsync("APP", "old_pass", "new_pass", deviceInfo, nonces);

        Assert.False(result.Success);
        Assert.Equal(LinkChangePasswordResult.ErrorBadOldPassword, result.Error);
    }

    [Fact]
    public async Task ChangePassword_BadCrc_ReturnsFailureWithError()
    {
        var (client, transport) = CreateClient();
        var deviceInfo = DeviceWithHash();
        var nonces = new LinkAuthNonces
        {
            ClientNonce = "client_nonce",
            DeviceNonce = "device_nonce"
        };

        transport.FrameSent += frame =>
        {
            if (frame.Command == "CHPWD")
                transport.SimulateReceive(new LinkFrame("APP", "RETURN", "CHPWD", "ERR", "BAD_CRC"));
        };

        var result = await client.ChangePasswordAsync("APP", "old_pass", "new_pass", deviceInfo, nonces);

        Assert.False(result.Success);
        Assert.Equal(LinkChangePasswordResult.ErrorBadCrc, result.Error);
    }

    [Fact]
    public async Task ChangePassword_SendsCorrectHashesAndCrc()
    {
        var (client, transport) = CreateClient();
        var deviceInfo = DeviceWithHash();
        var nonces = new LinkAuthNonces
        {
            ClientNonce = "client_nonce",
            DeviceNonce = "device_nonce"
        };

        var expectedOldHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes("client_noncedevice_nonceold_pass")))
            .ToLowerInvariant();

        var expectedNewHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes("client_noncedevice_noncenew_pass")))
            .ToLowerInvariant();

        var expectedCrc = Crc32.HashToUInt32(
            Encoding.UTF8.GetBytes(expectedOldHash + expectedNewHash))
            .ToString("x8");

        transport.FrameSent += frame =>
        {
            if (frame.Command == "CHPWD")
                transport.SimulateReceive(new LinkFrame("APP", "RETURN", "CHPWD", "OK"));
        };

        await client.ChangePasswordAsync("APP", "old_pass", "new_pass", deviceInfo, nonces);

        var chpwdFrame = transport.SentFrames.Single(f => f.Command == "CHPWD");
        Assert.Equal(expectedOldHash, chpwdFrame.Arguments[0]);
        Assert.Equal(expectedNewHash, chpwdFrame.Arguments[1]);
        Assert.Equal(expectedCrc, chpwdFrame.Arguments[2]);
    }

    [Fact]
    public async Task ChangePassword_HashMethodNone_ThrowsInvalidOperation()
    {
        var (client, _) = CreateClient();
        var deviceInfo = new LinkDeviceInfo { HashMethod = "NONE" };
        var nonces = new LinkAuthNonces { ClientNonce = "a", DeviceNonce = "b" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ChangePasswordAsync("APP", "old", "new", deviceInfo, nonces));
    }

    [Fact]
    public async Task ChangePassword_UnsupportedHashMethod_ThrowsNotSupported()
    {
        var (client, _) = CreateClient();
        var deviceInfo = new LinkDeviceInfo { HashMethod = "MD5" };
        var nonces = new LinkAuthNonces { ClientNonce = "a", DeviceNonce = "b" };

        await Assert.ThrowsAsync<NotSupportedException>(
            () => client.ChangePasswordAsync("APP", "old", "new", deviceInfo, nonces));
    }

    [Fact]
    public void ComputeCrc32_ReturnsExpectedHex()
    {
        var input = "hello";
        var expected = Crc32.HashToUInt32(Encoding.UTF8.GetBytes(input)).ToString("x8");

        var result = ChangePasswordHelper.ComputeCrc32(input);

        Assert.Equal(expected, result);
    }
}
