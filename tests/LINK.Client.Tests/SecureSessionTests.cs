using System.Security.Cryptography;
using Link.Client.Extensions;
using Link.Client.Models;
using Link.Client.Security;
using Link.Client.Tests.Fakes;
using Link.Core.Frames;
using Link.Core.Framing;

namespace Link.Client.Tests;

public class SecureSessionTests
{
    private static async Task<(LinkClient Client, LoopbackByteTransport Transport, FakeV2Device Device)> CreateAsync()
    {
        var transport = new LoopbackByteTransport();
        var device = new FakeV2Device(transport.Inject);
        transport.DeviceInput = device.Input;
        var client = new LinkClient(new LinkClientOptions { Transport = transport, CommandTimeout = TimeSpan.FromSeconds(5) });
        await client.ConnectAsync();
        return (client, transport, device);
    }

    [Fact]
    public async Task GetV_OverV1_ExposesV2Capabilities()
    {
        var (client, transport, device) = await CreateAsync();
        var info = await client.GetDeviceInfoAsync("DRAGON");

        Assert.Equal(LinkWireFormat.V1Text, transport.WireFormat);
        Assert.True(info.SupportsV2);
        Assert.Equal(device.Fingerprint, info.Fingerprint);
        Assert.False(info.IsProvisioned);
    }

    [Fact]
    public async Task FullFlow_Provision_Authenticate_ChangePassword()
    {
        var (client, transport, device) = await CreateAsync();

        var session = await client.OpenSecureSessionAsync("DRAGON");
        Assert.Equal(LinkWireFormat.V2Binary, transport.WireFormat);
        Assert.NotNull(transport.Protector);
        Assert.Equal(device.Fingerprint, session.Fingerprint);
        Assert.False(session.IsProvisioned);

        await client.SetPasswordAsync("s3cret", iterations: 10_000);
        Assert.NotNull(device.Verifier);
        Assert.Equal(SecureSessionHelper_DeriveKey("s3cret", device.Salt, device.Iterations), device.Verifier);

        await client.CloseSecureSessionAsync();
        Assert.Null(client.Session);

        await client.OpenSecureSessionAsync("DRAGON");
        var locked = await client.SendCommandAsync("DRAGON", "SETLED", default, "ON");
        Assert.Equal(new[] { "ERR", "NOT_AUTH" }, locked.ReturnArguments);

        var bad = await client.AuthenticateSecureAsync("wrong");
        Assert.False(bad.Success);
        Assert.Equal(LinkSecureAuthResult.ErrorBadPassword, bad.Error);
        Assert.Equal(2, bad.RemainingAttempts);

        var ok = await client.AuthenticateSecureAsync("s3cret");
        Assert.True(ok.Success);
        Assert.True(client.Session!.IsAuthenticated);

        var led = await client.SendCommandAsync("DRAGON", "SETLED", default, "ON");
        Assert.True(led.IsEncrypted);
        Assert.Equal("OK", led.ReturnArguments[0]);

        await client.ChangePasswordSecureAsync("s3cret", "n3w", iterations: 10_000);
        Assert.Equal(SecureSessionHelper_DeriveKey("n3w", device.Salt, device.Iterations), device.Verifier);

        await client.OpenSecureSessionAsync("DRAGON");
        Assert.False((await client.AuthenticateSecureAsync("s3cret")).Success);
        Assert.True((await client.AuthenticateSecureAsync("n3w")).Success);
    }

    [Fact]
    public async Task InvalidSignature_IsRejected()
    {
        var (client, _, device) = await CreateAsync();
        device.CorruptSignature = true;
        await Assert.ThrowsAsync<LinkSecurityException>(() => client.OpenSecureSessionAsync("DRAGON"));
        Assert.Null(client.Session);
    }

    [Fact]
    public async Task ExpectedFingerprint_Mismatch_IsRejected()
    {
        var (client, _, _) = await CreateAsync();
        await Assert.ThrowsAsync<LinkSecurityException>(() =>
            client.OpenSecureSessionAsync("DRAGON", new LinkSecureSessionOptions { ExpectedFingerprint = "0011223344556677" }));
    }

    [Fact]
    public async Task TrustOnFirstUse_DetectsIdentityChange()
    {
        var (client, _, device) = await CreateAsync();
        var store = new LinkMemoryTrustStore();
        var options = new LinkSecureSessionOptions { TrustStore = store };

        var first = await client.OpenSecureSessionAsync("DRAGON", options);
        Assert.False(first.IsFingerprintPinned);
        Assert.Equal(device.Fingerprint, store.GetFingerprint("DRAGON/TEST-1"));

        var second = await client.OpenSecureSessionAsync("DRAGON", options);
        Assert.True(second.IsFingerprintPinned);

        // Un imposteur (autre clé d'identité) se présente avec le même UID
        device.Identity = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await Assert.ThrowsAsync<LinkSecurityException>(() => client.OpenSecureSessionAsync("DRAGON", options));

        var strict = new LinkSecureSessionOptions { TrustStore = new LinkMemoryTrustStore(), TrustPolicy = LinkTrustPolicy.Strict };
        await Assert.ThrowsAsync<LinkSecurityException>(() => client.OpenSecureSessionAsync("DRAGON", strict));
    }

    [Fact]
    public async Task EncryptedEvents_AreRaised()
    {
        var (client, _, device) = await CreateAsync();
        await client.OpenSecureSessionAsync("DRAGON");

        var received = new TaskCompletionSource<LinkFrame>();
        client.EventReceived += f => received.TrySetResult(f);
        device.SendEvent("TEMP", "22.5");

        var evt = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(evt.IsEvent);
        Assert.True(evt.IsEncrypted);
        Assert.Equal("TEMP", evt.Command);
        Assert.Equal("22.5", evt.Arguments[0]);
    }

    [Fact]
    public async Task V2_SameCommandConcurrently_IsCorrelatedBySequence()
    {
        var (client, _, _) = await CreateAsync();
        await client.OpenSecureSessionAsync("DRAGON");

        var tasks = Enumerable.Range(0, 10).Select(_ => client.SendCommandAsync("DRAGON", "PING")).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Equal("PONG", r.ReturnArguments[0]));
        Assert.Equal(10, results.Select(r => r.Sequence).Distinct().Count());
    }

    [Fact]
    public async Task PlaintextCommandOnSecureChannel_IsRefusedByDevice()
    {
        var (client, transport, _) = await CreateAsync();
        await client.OpenSecureSessionAsync("DRAGON");
        var cipher = transport.Protector;
        transport.Protector = null; // simulation d'un client qui oublierait de chiffrer
        var r = await client.SendCommandAsync("DRAGON", "PING");
        Assert.Equal(new[] { "ERR", "NO_SESSION" }, r.ReturnArguments);
        transport.Protector = cipher;
    }

    private static byte[] SecureSessionHelper_DeriveKey(string pwd, byte[] salt, int it)
        => Link.Client.Helpers.SecureSessionHelper.DeriveKey(pwd, salt, it);
}
