// Example: connect to a LINK device over TCP.
// Run the Python TCP simulator first:
//   python examples/LINK.Device.Simulator/link_tcp_simulator.py --app-id DRAGON --password password
//
// Then run this example:
//   dotnet run                         – connects to 127.0.0.1:5000 (default)
//   dotnet run -- 192.168.1.10 9000   – connects to a custom host/port

using Link.Client;
using Link.Client.Extensions;
using Link.Transport.Tcp;

string host = args.Length > 0 ? args[0] : "127.0.0.1";
int port = args.Length > 1 && int.TryParse(args[1], out int p) ? p : 5000;

Console.WriteLine($"Connecting to LINK device via TCP at {host}:{port} ...");

await using var client = new LinkClient(new LinkClientOptions
{
    Transport = new LinkTcpTransport(new LinkTcpOptions
    {
        Host = host,
        Port = port,
        ConnectTimeout = TimeSpan.FromSeconds(5)
    }),
    CommandTimeout = TimeSpan.FromSeconds(5)
});

await client.ConnectAsync();
Console.WriteLine("Connected.");

var dragon = client.WithAppId("DRAGON");

var info = await dragon.GetDeviceInfoAsync();
Console.WriteLine($"Device version : {info.Version}");
Console.WriteLine($"Device model   : {info.Model ?? "(not reported)"}");
Console.WriteLine($"Device UID     : {info.Uid ?? "(not reported)"}");

await dragon.AuthenticateAsync("password");
Console.WriteLine("Authenticated.");

var tempFrame = await dragon.SendAsync("GETTEMP");
Console.WriteLine($"Temperature    : {string.Join(":", tempFrame.ReturnArguments)}");

