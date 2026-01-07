using Link.Core.Frames;

namespace Link.Core.Transport;

public interface ILinkTransport : IAsyncDisposable
{
    event Action<LinkFrame> FrameReceived;

    Task OpenAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);

    Task SendAsync(LinkFrame frame, CancellationToken cancellationToken = default);

    bool IsOpen { get; }
}
