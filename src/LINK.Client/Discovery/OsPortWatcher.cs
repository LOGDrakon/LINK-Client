using System.IO.Ports;
using System.Management;
using System.Runtime.Versioning;

namespace Link.Client.Discovery;

/// <summary>
/// Surveille l'apparition / disparition des ports série.
/// Windows : évènements WMI (Plug and Play). Linux / macOS : scrutation périodique.
/// </summary>
public sealed class OsPortWatcher : IDisposable
{
    private readonly object _lock = new();
    private ManagementEventWatcher? _arrivalWatcher;
    private ManagementEventWatcher? _removalWatcher;
    private Timer? _pollTimer;
    private IReadOnlySet<string> _knownPorts = new HashSet<string>();

    public event Action<string>? PortAdded;
    public event Action<string>? PortRemoved;

    /// <summary>Intervalle de scrutation hors Windows.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    public OsPortWatcher()
    {
        if (OperatingSystem.IsWindows())
            CreateWmiWatchers();
    }

    [SupportedOSPlatform("windows")]
    private void CreateWmiWatchers()
    {
        // Arrivée d’un port COM
        _arrivalWatcher = new ManagementEventWatcher(
            new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2"));
        _arrivalWatcher.EventArrived += (_, _) => RefreshPorts();

        // Retrait d’un port COM
        _removalWatcher = new ManagementEventWatcher(
            new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3"));
        _removalWatcher.EventArrived += (_, _) => RefreshPorts();
    }

    private void RefreshPorts()
    {
        HashSet<string> added, removed;
        lock (_lock)
        {
            var currentPorts = SerialPort.GetPortNames().ToHashSet();
            added = currentPorts.Except(_knownPorts).ToHashSet();
            removed = _knownPorts.Except(currentPorts).ToHashSet();
            _knownPorts = currentPorts;
        }

        foreach (var port in added)
            PortAdded?.Invoke(port);

        foreach (var port in removed)
            PortRemoved?.Invoke(port);
    }

    public void Start()
    {
        lock (_lock)
            _knownPorts = SerialPort.GetPortNames().ToHashSet();

        if (OperatingSystem.IsWindows())
        {
            _arrivalWatcher!.Start();
            _removalWatcher!.Start();
        }
        else
        {
            _pollTimer = new Timer(_ => RefreshPorts(), null, PollInterval, PollInterval);
        }

        // Signal ports that are already present so that discovery scans
        // them immediately (not only when a PnP event fires later).
        foreach (var port in _knownPorts)
            PortAdded?.Invoke(port);
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
        if (OperatingSystem.IsWindows())
        {
            _arrivalWatcher?.Stop();
            _removalWatcher?.Stop();
            _arrivalWatcher?.Dispose();
            _removalWatcher?.Dispose();
        }
    }
}
