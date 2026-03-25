using System.Net.Sockets;
using System.Text;
using Link.Core.Frames;
using Link.Core.Parsing;
using Link.Core.Transport;

namespace Link.Transport.Tcp;

public sealed class LinkTcpTransport : ILinkTransport
{
    private readonly LinkTcpOptions _options;
    private readonly LinkParser _parser = new();
    private readonly Encoding _encoding = Encoding.ASCII;
    private readonly int _maxPacketSize;

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;

    public event Action<LinkFrame>? FrameReceived;
    public event Action<Exception>? TransportError;

    public bool IsOpen => _tcpClient?.Connected == true;

    public LinkTcpTransport(LinkTcpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Host))
            throw new ArgumentException("Host cannot be empty.", nameof(options));
        if (options.Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(options), "Port must be between 1 and 65535.");

        _options = options;
        _maxPacketSize = options.MaxPacketSize;
        _parser.FrameReceived += f => FrameReceived?.Invoke(f);
    }

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (IsOpen)
            throw new InvalidOperationException("Transport is already open.");

        _tcpClient = new TcpClient();

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(_options.ConnectTimeout);

        await _tcpClient.ConnectAsync(_options.Host, _options.Port, connectCts.Token)
            .ConfigureAwait(false);

        _stream = _tcpClient.GetStream();
        _readCts = new CancellationTokenSource();
        _readTask = ReadLoopAsync(_readCts.Token);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_readCts is not null)
        {
            await _readCts.CancelAsync().ConfigureAwait(false);
            _readCts.Dispose();
            _readCts = null;
        }

        if (_readTask is not null)
        {
            try { await _readTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            _readTask = null;
        }

        _stream?.Dispose();
        _stream = null;
        _tcpClient?.Dispose();
        _tcpClient = null;
    }

    public async Task SendAsync(LinkFrame frame, CancellationToken cancellationToken = default)
    {
        if (!IsOpen || _stream is null)
            throw new InvalidOperationException("TCP transport is not open.");

        var data = _encoding.GetBytes(frame.ToString());

        if (_maxPacketSize <= 0 || data.Length <= _maxPacketSize)
        {
            await _stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            for (int offset = 0; offset < data.Length; offset += _maxPacketSize)
            {
                int chunkSize = Math.Min(_maxPacketSize, data.Length - offset);
                await _stream.WriteAsync(
                    data.AsMemory(offset, chunkSize), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested && _stream is not null)
            {
                int bytesRead = await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (bytesRead == 0)
                    break;

                var text = _encoding.GetString(buffer, 0, bytesRead);
                _parser.Feed(text.AsSpan());
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            TransportError?.Invoke(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
    }
}
