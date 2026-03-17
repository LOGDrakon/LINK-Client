using Link.Client.Helpers;
using Link.Core.Transport;

namespace Link.Client.Discovery;

public sealed class LinkDeviceWatcher : IAsyncDisposable
{
    private readonly OsPortWatcher _osWatcher;
    private readonly LinkDiscoveryHelper _discovery;
    private readonly string _appIdFilter;

    public LinkDeviceCollection Devices { get; } = new();

    public event Action<LinkDetectedDevice>? DeviceAdded;
    public event Action<LinkDetectedDevice>? DeviceRemoved;

    public LinkDeviceWatcher(
        Func<string, ILinkTransport> transportFactory,
        TimeSpan timeout,
        string appIdFilter)
    {
        _appIdFilter = appIdFilter;

        _discovery = new LinkDiscoveryHelper(transportFactory, timeout);
        _osWatcher = new OsPortWatcher();

        _osWatcher.PortAdded += async port => await TryAddDeviceAsync(port);
        _osWatcher.PortRemoved += RemoveDevice;
    }

    public void Start() => _osWatcher.Start();

    private async Task TryAddDeviceAsync(string port)
    {
        var device = await _discovery.TryDetectAsync(port, _appIdFilter);
        if (device == null)
            return;

        Devices.Add(device);
        DeviceAdded?.Invoke(device);
    }

    private void RemoveDevice(string port)
    {
        var device = Devices.FirstOrDefault(d => d.PortName == port);
        if (device == null)
            return;

        Devices.Remove(device);
        DeviceRemoved?.Invoke(device);
    }

    public ValueTask DisposeAsync()
    {
        _osWatcher.Dispose();
        return ValueTask.CompletedTask;
    }
}
