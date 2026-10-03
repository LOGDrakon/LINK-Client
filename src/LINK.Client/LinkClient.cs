using Link.Client.Internal;
using Link.Client.Security;
using Link.Core.Frames;
using Link.Core.Framing;
using Link.Core.Transport;
using System.Collections.Concurrent;

namespace Link.Client;

public class LinkClient : IAsyncDisposable
{
    private readonly ILinkTransport _transport;
    private readonly ConcurrentDictionary<string, PendingCommand> _pending = new();
    private readonly ConcurrentDictionary<ushort, PendingCommand> _pendingBySeq = new();
    private readonly TimeSpan _timeout;
    private int _sequence;

    public LinkClient(LinkClientOptions options)
    {
        _transport = options.Transport;
        _timeout = options.CommandTimeout;

        _transport.FrameReceived += OnFrameReceived;
    }

    /// <summary>Transport sous-jacent.</summary>
    public ILinkTransport Transport => _transport;

    /// <summary>Délai d'attente par défaut d'une réponse.</summary>
    public TimeSpan CommandTimeout => _timeout;

    /// <summary>Session sécurisée LINK v2 en cours (null si aucune).</summary>
    public LinkSecureSession? Session { get; internal set; }

    /// <summary>
    /// Trame non sollicitée reçue du device (évènement v2 ou trame v1 autre que RETURN).
    /// </summary>
    public event Action<LinkFrame>? EventReceived;

    public Task ConnectAsync(CancellationToken ct = default)
        => _transport.OpenAsync(ct);

    public Task<LinkFrame> SendCommandAsync(
        string appId,
        string command,
        CancellationToken ct = default,
        params string[] args)
        => SendCommandAsync(appId, command, null, ct, args);

    /// <summary>Envoie une commande et attend son RETURN (délai spécifique possible, ex. PBKDF2 côté device).</summary>
    public async Task<LinkFrame> SendCommandAsync(
        string appId,
        string command,
        TimeSpan? timeout,
        CancellationToken ct = default,
        params string[] args)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException(nameof(appId));

        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException(nameof(command));

        bool v2 = _transport is ILinkSecureTransport { WireFormat: LinkWireFormat.V2Binary };
        var pending = new PendingCommand(command);
        LinkFrame frame;
        string? key = null;
        ushort seq = 0;

        if (v2)
        {
            seq = NextSequence();
            frame = new LinkFrame(appId, command, args) { Version = 2, Sequence = seq };
            if (!_pendingBySeq.TryAdd(seq, pending))
                throw new InvalidOperationException($"Sequence already pending: {seq}");
        }
        else
        {
            frame = new LinkFrame(appId, command, args);
            key = BuildPendingKey(appId, command);
            if (!_pending.TryAdd(key, pending))
                throw new InvalidOperationException($"Command already pending: {command} for {appId}");
        }

        try
        {
            await _transport.SendAsync(frame, ct).ConfigureAwait(false);

            using var timeoutCts = new CancellationTokenSource(timeout ?? _timeout);
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
            if (key is not null)
                _pending.TryRemove(key, out _);
            else
                _pendingBySeq.TryRemove(seq, out _);
        }
    }

    private ushort NextSequence()
    {
        // 0 est réservé aux évènements
        while (true)
        {
            var seq = (ushort)Interlocked.Increment(ref _sequence);
            if (seq != 0)
                return seq;
        }
    }

    private void OnFrameReceived(LinkFrame frame)
    {
        if (frame.IsEvent || !frame.IsReturn)
        {
            EventReceived?.Invoke(frame);
            return;
        }

        if (frame.Version == 2 && frame.Sequence != 0)
        {
            if (_pendingBySeq.TryRemove(frame.Sequence, out var bySeq))
                bySeq.Tcs.TrySetResult(frame);
            return;
        }

        if (frame.ReturnedCommand is null || frame.AppId is null)
            return;

        var key = BuildPendingKey(frame.AppId, frame.ReturnedCommand);

        if (_pending.TryRemove(key, out var pending))
            pending.Tcs.TrySetResult(frame);
    }

    private static string BuildPendingKey(string appId, string command)
        => $"{appId}:{command}";

    public async ValueTask DisposeAsync()
    {
        Session?.Dispose();
        Session = null;
        await _transport.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
