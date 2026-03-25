namespace Link.Client.Models;

public sealed record LinkAuthResult
{
    public LinkSecurityState State { get; init; } = LinkSecurityState.Locked();
    public LinkAuthNonces Nonces { get; init; } = new();
}
