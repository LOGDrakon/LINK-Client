using Link.Core.Frames;
using Link.Core.Internal;
using Link.Core.Transport;
using LINK.Client.Internal;
using System.Collections.Concurrent;

namespace Link.Client;

public class LinkClient : IAsyncDisposable
{
    private readonly ILinkTransport _transport;
    private readonly ConcurrentDictionary<string, PendingCommand> _pending = new();
    private readonly TimeSpan _timeout;

    public LinkClient(LinkClientOptions options)
    {
        _transport = options.Transport;
        _timeout = options.CommandTimeout;

        _transport.FrameReceived += OnFrameReceived;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _transport.OpenAsync(ct);
    }

    public async Task<LinkFrame> SendCommandAsync(
        string appId,
        string command,
        params string[] args)
    {
        var frame = new LinkFrame(appId, command, args);
        var pending = new PendingCommand(command);

        if (!_pending.TryAdd(command, pending))
            throw new InvalidOperationException($"Command already pending: {command}");

        await _transport.SendAsync(frame);

        using var cts = new CancellationTokenSource(_timeout);
        using (cts.Token.Register(() =>
            pending.Tcs.TrySetException(new TimeoutException())))
        {
            return await pending.Tcs.Task;
        }
    }

    private void OnFrameReceived(LinkFrame frame)
    {
        if (!frame.IsReturn || frame.ReturnedCommand is null)
            return;

        if (_pending.TryRemove(frame.ReturnedCommand, out var pending))
        {
            pending.Tcs.TrySetResult(frame);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _transport.DisposeAsync();
    }
}
