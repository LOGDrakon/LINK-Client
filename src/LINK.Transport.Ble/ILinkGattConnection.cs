namespace Link.Transport.Ble;

/// <summary>
/// Connexion GATT fournie par la plateforme (Android BluetoothGatt, Windows.Devices.Bluetooth,
/// Plugin.BLE, BlueZ…). Le SDK n'en dépend pas directement.
/// </summary>
public interface ILinkGattConnection : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>Taille maximale d'une écriture (MTU négocié − 3).</summary>
    int MaxWriteSize { get; }

    /// <summary>Connecte, négocie le MTU, découvre le service du profil et active les notifications TX.</summary>
    Task ConnectAsync(LinkBleProfile profile, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Écrit sur la caractéristique RX (Write Without Response de préférence).</summary>
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>Notification reçue sur la caractéristique TX.</summary>
    event Action<byte[]>? NotificationReceived;

    /// <summary>Lien radio perdu.</summary>
    event Action? Disconnected;
}
