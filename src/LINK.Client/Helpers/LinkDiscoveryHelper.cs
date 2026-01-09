using Link.Client.Discovery;
using Link.Client.Extensions;
using Link.Core.Transport;

namespace Link.Client.Helpers;

public sealed class LinkDiscoveryHelper
{
    private readonly Func<string, ILinkTransport> _transportFactory;
    private readonly TimeSpan _timeout;

    public LinkDiscoveryHelper(
        Func<string, ILinkTransport> transportFactory,
        TimeSpan? timeout = null)
    {
        _transportFactory = transportFactory;
        _timeout = timeout ?? TimeSpan.FromMilliseconds(800);
    }

    public async Task<IReadOnlyList<LinkDetectedDevice>> ScanAsync(
        string? appIdFilter = null,
        CancellationToken cancellationToken = default)
    {
        var ports = Discovery.LinkPortScanner.GetAvailablePorts();

        var tasks = ports.Select(port =>
            TryDetectAsync(port, appIdFilter, cancellationToken));

        var results = await Task.WhenAll(tasks);

        return results
            .Where(d => d != null)
            .Cast<LinkDetectedDevice>()
            .ToArray();
    }

    private async Task<LinkDetectedDevice?> TryDetectAsync(
        string port,
        string? appIdFilter,
        CancellationToken ct)
    {
        try
        {
            await using var transport = _transportFactory(port);

            var client = new LinkClient(new LinkClientOptions
            {
                Transport = transport,
                CommandTimeout = _timeout
            });

            await client.ConnectAsync(ct);

            var info = await client.GetDeviceInfoAsync("AUTO");

            if (appIdFilter != null &&
                !string.Equals(info.AppId, appIdFilter, StringComparison.OrdinalIgnoreCase))
                return null;

            return new LinkDetectedDevice
            {
                PortName = port,
                AppId = info.AppId,
                DeviceInfo = info
            };
        }
        catch
        {
            return null;
        }
    }
}
