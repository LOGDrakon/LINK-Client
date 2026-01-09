using System.IO.Ports;

namespace Link.Client.Discovery;

public static class LinkPortScanner
{
    public static IReadOnlyList<string> GetAvailablePorts()
        => SerialPort.GetPortNames()
                     .OrderBy(p => p)
                     .ToArray();
}
