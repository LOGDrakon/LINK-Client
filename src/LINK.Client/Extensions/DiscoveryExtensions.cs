using Link.Client.Discovery;
using Link.Client.Helpers;
using Link.Core.Transport;

namespace Link.Client.Extensions;

public static class DiscoveryExtensions
{
    public static Task<IReadOnlyList<LinkDetectedDevice>> ScanForLinkDevicesAsync(
        Func<string, ILinkTransport> transportFactory,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var helper = new LinkDiscoveryHelper(transportFactory, timeout);
        return helper.ScanAsync(null, cancellationToken);
    }
}
