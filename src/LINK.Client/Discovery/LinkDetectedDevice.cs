using Link.Client.Models;

namespace Link.Client.Discovery;

public sealed record LinkDetectedDevice
{
    public string PortName { get; init; } = string.Empty;
    public string AppId { get; init; } = string.Empty;
    public LinkDeviceInfo DeviceInfo { get; init; } = default!;
}
