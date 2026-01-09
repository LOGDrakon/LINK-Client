//This project is an example of using the Link.Client library to discover devices via serial transport.
//Use it as a reference for building your own applications with Link.Client.

using Link.Client.Discovery;
using Link.Transport.Serial;

var watcher = new LinkDeviceWatcher(
    port => new LinkSerialTransport(new LinkSerialOptions
    {
        PortName = port,
        BaudRate = 115200
    }),
    timeout: TimeSpan.FromMilliseconds(800),
    appIdFilter: "DRAGON");

watcher.DeviceAdded += d =>
    Console.WriteLine($"[+] {d.PortName} - {d.DeviceInfo.Model}");

watcher.DeviceRemoved += d =>
    Console.WriteLine($"[-] {d.PortName}");

watcher.Start();

Console.WriteLine("Scanning... Press Enter to exit");
Console.ReadLine();

await watcher.DisposeAsync();
