//This project is an example of using the Link.Client library to connect to a device via serial transport and retrieve device information.
//Use it as a reference for building your own applications with Link.Client.

using Link.Client;
using Link.Client.Extensions;
using Link.Transport.Serial;

var transport = new LinkSerialTransport(new LinkSerialOptions
{
    PortName = "COM3",
    BaudRate = 115200
});

var client = new LinkClient(new LinkClientOptions
{
    Transport = transport
});

await client.ConnectAsync();

var dragon = client.WithAppId("DRAGON");

var info = await dragon.GetDeviceInfoAsync();
Console.WriteLine($"Version: {info.Version}");

await dragon.AuthenticateAsync("password");

var frame = await dragon.SendAsync("GETTEMP");
Console.WriteLine(frame.ToString());

var task = client.SendCommandAsync(appId: "APP", command: "GETV", ct: CancellationToken.None);
