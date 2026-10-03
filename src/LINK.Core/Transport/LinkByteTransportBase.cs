using Link.Core.Frames;
using Link.Core.Framing;
using Link.Core.Security;

namespace Link.Core.Transport;

/// <summary>
/// Base des transports « flux d'octets » : gère le tramage v1/v2, le découpage
/// en paquets (MTU USB/BLE) et le chiffrement de session.
/// Les classes dérivées n'implémentent que l'ouverture, la fermeture et l'écriture brute,
/// et appellent <see cref="OnBytesReceived"/> à la réception.
/// </summary>
public abstract class LinkByteTransportBase : ILinkSecureTransport
{
    private readonly LinkStreamDecoder _decoder;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private ILinkFrameProtector? _protector;

    protected LinkByteTransportBase(int maxPacketSize = 0, int maxFrameSize = LinkFrameCodec.DefaultMaxFrameSize)
    {
        MaxPacketSize = maxPacketSize;
        _decoder = new LinkStreamDecoder { MaxFrameSize = maxFrameSize };
        _decoder.FrameReceived += f => FrameReceived?.Invoke(f);
        _decoder.FrameRejected += b => FrameRejected?.Invoke(b);
    }

    public event Action<LinkFrame>? FrameReceived;
    public event Action<Exception>? TransportError;

    /// <summary>Bloc reçu mais rejeté (CRC, tag GCM, rejeu…).</summary>
    public event Action<byte[]>? FrameRejected;

    public LinkWireFormat WireFormat { get; set; } = LinkWireFormat.V1Text;

    public ILinkFrameProtector? Protector
    {
        get => _protector;
        set
        {
            _protector = value;
            _decoder.Protector = value;
        }
    }

    /// <summary>Taille maximale d'une écriture (64 = USB FS, MTU-3 en BLE). 0 = pas de découpage.</summary>
    public int MaxPacketSize { get; protected set; }

    public abstract bool IsOpen { get; }

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        _decoder.Reset();
        await OpenCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        Protector = null;
        await CloseCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAsync(LinkFrame frame, CancellationToken cancellationToken = default)
    {
        if (!IsOpen)
            throw new InvalidOperationException("Transport is not open.");

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Encodage sous verrou : l'ordre des compteurs de chiffrement doit suivre l'ordre d'émission.
            var data = WireFormat == LinkWireFormat.V2Binary
                ? LinkFrameCodec.EncodeV2(frame, LinkPacketFlags.None, Protector)
                : LinkFrameCodec.EncodeV1(frame);

            if (MaxPacketSize <= 0 || data.Length <= MaxPacketSize)
            {
                await WriteAsync(data, cancellationToken).ConfigureAwait(false);
                return;
            }

            for (int offset = 0; offset < data.Length; offset += MaxPacketSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int size = Math.Min(MaxPacketSize, data.Length - offset);
                await WriteAsync(data.AsMemory(offset, size), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>À appeler par la classe dérivée pour chaque bloc d'octets reçu.</summary>
    protected void OnBytesReceived(ReadOnlySpan<byte> data) => _decoder.Feed(data);

    protected void OnTransportError(Exception ex) => TransportError?.Invoke(ex);

    protected abstract Task OpenCoreAsync(CancellationToken cancellationToken);
    protected abstract Task CloseCoreAsync(CancellationToken cancellationToken);
    protected abstract ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    public virtual async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _writeLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
