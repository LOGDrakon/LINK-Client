using System.IO.Ports;
using System.Management;

namespace Link.Client.Discovery;

public sealed class OsPortWatcher : IDisposable
{
    private readonly ManagementEventWatcher _arrivalWatcher;
    private readonly ManagementEventWatcher _removalWatcher;

    public event Action<string>? PortAdded;
    public event Action<string>? PortRemoved;

    public OsPortWatcher()
    {
        // Arrivée d’un port COM
        _arrivalWatcher = new ManagementEventWatcher(
            new WqlEventQuery(
                "SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2"));

        _arrivalWatcher.EventArrived += (_, _) => RefreshPorts();

        // Retrait d’un port COM
        _removalWatcher = new ManagementEventWatcher(
            new WqlEventQuery(
                "SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3"));

        _removalWatcher.EventArrived += (_, _) => RefreshPorts();
    }

    private IReadOnlySet<string> _knownPorts = new HashSet<string>();

    private void RefreshPorts()
    {
        var currentPorts = SerialPort.GetPortNames().ToHashSet();

        foreach (var added in currentPorts.Except(_knownPorts))
            PortAdded?.Invoke(added);

        foreach (var removed in _knownPorts.Except(currentPorts))
            PortRemoved?.Invoke(removed);

        _knownPorts = currentPorts;
    }

    public void Start()
    {
        _knownPorts = SerialPort.GetPortNames().ToHashSet();
        _arrivalWatcher.Start();
        _removalWatcher.Start();
    }

    public void Dispose()
    {
        _arrivalWatcher.Stop();
        _removalWatcher.Stop();
        _arrivalWatcher.Dispose();
        _removalWatcher.Dispose();
    }
}
