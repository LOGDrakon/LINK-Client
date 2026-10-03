using System.Net.Sockets;
using Link.Core.Transport;

namespace Link.Transport.Tcp;

/// <summary>Transport TCP (device Wi-Fi/Ethernet, simulateur).</summary>
public sealed class LinkTcpTransport : LinkByteTransportBase
{
    private readonly LinkTcpOptions _options;

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;

    /// <summary>La connexion a été fermée par le device.</summary>
    public event Action? Disconnected;

    public override bool IsOpen => _tcpClient?.Connected == true;

    public LinkTcpTransport(LinkTcpOptions options)
        : base(options?.MaxPacketSize ?? 0, options?.MaxFrameSize ?? Link.Core.Framing.LinkFrameCodec.DefaultMaxFrameSize)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Host))
            throw new ArgumentException("Host cannot be empty.", nameof(options));
        if (options.Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(options), "Port must be between 1 and 65535.");

        _options = options;
        WireFormat = options.WireFormat;
    }

    protected override async Task OpenCoreAsync(CancellationToken cancellationToken)
    {
        if (IsOpen)
            throw new InvalidOperationException("Transport is already open.");

        _tcpClient = new TcpClient { NoDelay = true };

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(_options.ConnectTimeout);

        await _tcpClient.ConnectAsync(_options.Host, _options.Port, connectCts.Token)
            .ConfigureAwait(false);

        _stream = _tcpClient.GetStream();
        _readCts = new CancellationTokenSource();
        _readTask = ReadLoopAsync(_readCts.Token);
    }

    protected override async Task CloseCoreAsync(CancellationToken cancellationToken)
    {
        if (_readCts is not null)
        {
            await _readCts.CancelAsync().ConfigureAwait(false);
        }

        _stream?.Dispose();
        _stream = null;
        _tcpClient?.Dispose();
        _tcpClient = null;

        if (_readTask is not null)
        {
            try { await _readTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            _readTask = null;
        }

        _readCts?.Dispose();
        _readCts = null;
    }

    protected override async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (_stream is null)
            throw new InvalidOperationException("TCP transport is not open.");
        await _stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];
        var stream = _stream;
        try
        {
            while (!ct.IsCancellationRequested && stream is not null)
            {
                int bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    Disconnected?.Invoke();
                    break;
                }

                OnBytesReceived(buffer.AsSpan(0, bytesRead));
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal
        }
        catch (ObjectDisposedException)
        {
            // Fermeture pendant la lecture
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                OnTransportError(ex);
        }
    }
}
