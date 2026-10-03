using System.Collections.Concurrent;

namespace Link.Client.Security;

public sealed class LinkMemoryTrustStore : ILinkTrustStore
{
    private readonly ConcurrentDictionary<string, string> _pins = new(StringComparer.OrdinalIgnoreCase);

    public string? GetFingerprint(string deviceKey) => _pins.TryGetValue(deviceKey, out var fp) ? fp : null;

    public void SetFingerprint(string deviceKey, string fingerprint) => _pins[deviceKey] = fingerprint;

    public void Remove(string deviceKey) => _pins.TryRemove(deviceKey, out _);
}
