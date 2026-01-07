using Link.Core.Transport;

namespace Link.Client;

public sealed class LinkClientOptions
{
    public required ILinkTransport Transport { get; init; }
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(2);
}
