using Link.Client.Internal;
using Link.Core.Frames;
using Link.Core.Transport;
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

    public Task ConnectAsync(CancellationToken ct = default)
        => _transport.OpenAsync(ct);

    public async Task<LinkFrame> SendCommandAsync(
        string appId,
        string command,
        CancellationToken ct = default,
        params string[] args)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException(nameof(appId));

        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException(nameof(command));

        var frame = new LinkFrame(appId, command, args);
        var key = BuildPendingKey(appId, command);
        var pending = new PendingCommand(command);

        if (!_pending.TryAdd(key, pending))
            throw new InvalidOperationException($"Command already pending: {command} for {appId}");

        try
        {
            await _transport.SendAsync(frame, ct).ConfigureAwait(false);

            using var timeoutCts = new CancellationTokenSource(_timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            await using (linkedCts.Token.Register(() => pending.Tcs.TrySetCanceled(linkedCts.Token)))
            {
                return await pending.Tcs.Task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Timeout waiting for RETURN:{command} ({appId})");
        }
        finally
        {
            _pending.TryRemove(key, out _);
        }
    }

    private void OnFrameReceived(LinkFrame frame)
    {
        if (!frame.IsReturn || frame.ReturnedCommand is null || frame.AppId is null)
            return;

        var key = BuildPendingKey(frame.AppId, frame.ReturnedCommand);

        if (_pending.TryRemove(key, out var pending))
            pending.Tcs.TrySetResult(frame);
    }

    private static string BuildPendingKey(string appId, string command)
        => $"{appId}:{command}";

    public ValueTask DisposeAsync()
        => _transport.DisposeAsync();
}
