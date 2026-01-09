//This example demonstrates how to connect to a LINK device over a serial transport,
//authenticate if the device is locked, and negotiate encryption using AES128.
//Use it as a reference for building your own applications with Link.Client.

using Link.Client.Extensions;
using Link.Transport.Serial;

var transport = new LinkSerialTransport(new LinkSerialOptions
{
    PortName = "COM3"
});

var client = new LinkClient(new LinkClientOptions
{
    Transport = transport
});

await client.ConnectAsync();

var info = await client.GetDeviceInfoAsync("DRAGON");

if (info.IsLocked)
{
    var state = await client.AuthenticateAsync("DRAGON", "1234");
    Console.WriteLine($"Authenticated: {state.IsAuthenticated}");
}

var crypto = await client.NegotiateEncryptionAsync(
    "DRAGON",
    info,
    mode => mode == "AES128" ? new Aes128CryptoProvider(key) : null);

Console.WriteLine($"Crypto enabled: {crypto.IsEnabled}");
