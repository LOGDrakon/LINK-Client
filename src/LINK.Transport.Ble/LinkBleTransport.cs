using Link.Core.Framing;
using Link.Core.Transport;

namespace Link.Transport.Ble;

/// <summary>
/// Transport LINK sur Bluetooth Low Energy : le flux d'octets est découpé
/// selon le MTU négocié et reconstitué à partir des notifications.
/// </summary>
public sealed class LinkBleTransport : LinkByteTransportBase
{
    private readonly ILinkGattConnection _connection;
    private readonly LinkBleProfile _profile;

    /// <summary>Le lien BLE a été perdu (la session LINK est alors invalide).</summary>
    public event Action? Disconnected;

    public LinkBleTransport(ILinkGattConnection connection, LinkBleProfile? profile = null,
                            LinkWireFormat wireFormat = LinkWireFormat.V2Binary)
        : base(maxPacketSize: 20)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _profile = profile ?? LinkBleProfile.Link;
        WireFormat = wireFormat;

        _connection.NotificationReceived += data => OnBytesReceived(data);
        _connection.Disconnected += () =>
        {
            Protector = null;
            Disconnected?.Invoke();
        };
    }

    public LinkBleProfile Profile => _profile;

    public override bool IsOpen => _connection.IsConnected;

    protected override async Task OpenCoreAsync(CancellationToken cancellationToken)
    {
        await _connection.ConnectAsync(_profile, cancellationToken).ConfigureAwait(false);
        MaxPacketSize = Math.Max(20, _connection.MaxWriteSize);
    }

    protected override Task CloseCoreAsync(CancellationToken cancellationToken)
        => _connection.IsConnected ? _connection.DisconnectAsync(cancellationToken) : Task.CompletedTask;

    protected override async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        => await _connection.WriteAsync(data, cancellationToken).ConfigureAwait(false);

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}
