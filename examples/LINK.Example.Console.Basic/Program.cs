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

Console.WriteLine("Connected");

var info = await client.GetDeviceInfoAsync("DRAGON");

Console.WriteLine($"Model: {info.Model}");
Console.WriteLine($"Version: {info.Version}");

await client.DisposeAsync();
