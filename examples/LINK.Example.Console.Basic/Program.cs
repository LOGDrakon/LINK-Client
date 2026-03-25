//This project is an example of using the Link.Client library to connect to a device via serial or TCP transport.
//Use it as a reference for building your own applications with Link.Client.
//
// Usage:
//   dotnet run                          – serial mode (COM3, 115200 baud)
//   dotnet run -- --tcp                 – TCP mode (127.0.0.1:5000)
//   dotnet run -- --tcp 192.168.1.10 9000  – TCP mode with custom host/port

using Link.Client;
using Link.Client.Extensions;
using Link.Core.Transport;
using Link.Transport.Serial;
using Link.Transport.Tcp;

bool useTcp = args.Any(a => a.Equals("--tcp", StringComparison.OrdinalIgnoreCase));

ILinkTransport transport;

if (useTcp)
{
    string host = "127.0.0.1";
    int port = 5000;

    int tcpIndex = Array.FindIndex(args, a => a.Equals("--tcp", StringComparison.OrdinalIgnoreCase));
    if (tcpIndex >= 0 && tcpIndex + 1 < args.Length)
        host = args[tcpIndex + 1];
    if (tcpIndex >= 0 && tcpIndex + 2 < args.Length && int.TryParse(args[tcpIndex + 2], out int parsedPort))
        port = parsedPort;

    Console.WriteLine($"Using TCP transport ({host}:{port})");
    transport = new LinkTcpTransport(new LinkTcpOptions
    {
        Host = host,
        Port = port,
        // MaxPacketSize = 64 (default) — chunks writes for STM32 USB FS compatibility
    });
}
else
{
    Console.WriteLine("Using Serial transport (COM3)");
    transport = new LinkSerialTransport(new LinkSerialOptions
    {
        PortName = "COM3",
        BaudRate = 115200,
        // MaxPacketSize = 64 (default) — chunks writes for STM32 USB FS compatibility
    });
}

var client = new LinkClient(new LinkClientOptions
{
    Transport = transport
});

await client.ConnectAsync();

var dragon = client.WithAppId("DRAGON");

var info = await dragon.GetDeviceInfoAsync();
Console.WriteLine($"Version: {info.Version}");
Console.WriteLine($"Hash method: {info.HashMethod}");

var authResult = await dragon.AuthenticateAsync("password", info);
Console.WriteLine($"Authenticated: {authResult.State.IsAuthenticated}");

// Change password after successful authentication
if (authResult.State.IsAuthenticated)
{
    var chpwd = await dragon.ChangePasswordAsync("password", "newpass", info, authResult.Nonces);

    if (chpwd.Success)
        Console.WriteLine("Password changed successfully.");
    else
        Console.WriteLine($"Password change failed: {chpwd.Error}");
        // chpwd.Error is "BAD_OLD_PWD" or "BAD_CRC"
}

var frame = await dragon.SendAsync("GETTEMP");
Console.WriteLine(frame.ToString());

var task = client.SendCommandAsync(appId: "APP", command: "GETV", ct: CancellationToken.None);

