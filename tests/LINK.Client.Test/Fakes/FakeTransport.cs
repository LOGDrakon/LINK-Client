using Link.Core.Frames;
using Link.Core.Transport;

namespace Link.Client.Tests.Fakes;

internal sealed class FakeTransport : ILinkTransport
{
    public event Action<LinkFrame>? FrameReceived;
    public bool IsOpen { get; private set; }

    public Task OpenAsync(CancellationToken _ = default)
    {
        IsOpen = true;
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken _ = default)
    {
        IsOpen = false;
        return Task.CompletedTask;
    }

    public Task SendAsync(LinkFrame frame, CancellationToken _ = default)
    {
        // Rien par défaut — le test déclenchera les réponses
        return Task.CompletedTask;
    }

    public void SimulateReceive(LinkFrame frame)
        => FrameReceived?.Invoke(frame);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
