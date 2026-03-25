//This example demonstrates how to connect to a LINK device over a serial transport,
//authenticate if the device is locked, and negotiate encryption using AES128.
//Use it as a reference for building your own applications with Link.Client.

using Link.Client.Extensions;
using Link.Transport.Serial;

var transport = new LinkSerialTransport(new LinkSerialOptions
{
    PortName = "COM3",
    // MaxPacketSize = 64 (default) — chunks writes for STM32 USB FS compatibility
});

var client = new LinkClient(new LinkClientOptions
{
    Transport = transport
});

await client.ConnectAsync();

var info = await client.GetDeviceInfoAsync("DRAGON");

if (info.IsLocked)
{
    var authResult = await client.AuthenticateAsync("DRAGON", "1234", info);
    Console.WriteLine($"Authenticated: {authResult.State.IsAuthenticated}");
    // authResult.Nonces can be reused for subsequent authentications

    // Change the password (requires prior authentication)
    if (authResult.State.IsAuthenticated)
    {
        var chpwd = await client.ChangePasswordAsync(
            "DRAGON", "1234", "5678", info, authResult.Nonces);

        if (chpwd.Success)
            Console.WriteLine("Password changed successfully.");
        else
            Console.WriteLine($"Password change failed: {chpwd.Error}");
            // chpwd.Error is "BAD_OLD_PWD" or "BAD_CRC"
    }
}

var crypto = await client.NegotiateEncryptionAsync(
    "DRAGON",
    info,
    mode => mode == "AES128" ? new Aes128CryptoProvider(key) : null);

Console.WriteLine($"Crypto enabled: {crypto.IsEnabled}");
