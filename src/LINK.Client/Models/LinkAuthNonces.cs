namespace Link.Client.Models;

public sealed record LinkAuthNonces
{
    public string ClientNonce { get; init; } = string.Empty;
    public string DeviceNonce { get; init; } = string.Empty;
}
