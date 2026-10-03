using Android.Bluetooth;
using Android.Content;
using Link.Transport.Ble;
using Java.Util;

namespace Link.Transport.Android;

/// <summary>
/// Connexion GATT Android (<see cref="BluetoothGatt"/>) pour <see cref="LinkBleTransport"/>.
/// Les opérations GATT Android ne supportent qu'une requête en vol : elles sont sérialisées ici.
/// </summary>
/// <remarks>
/// Permissions : BLUETOOTH_CONNECT (Android 12+). Voir <see cref="LinkAndroidPermissions"/>.
/// </remarks>
public sealed class AndroidGattConnection : ILinkGattConnection
{
    private static readonly UUID ClientConfigDescriptor = UUID.FromString("00002902-0000-1000-8000-00805f9b34fb")!;

    private readonly Context _context;
    private readonly BluetoothDevice _device;
    private readonly SemaphoreSlim _gattLock = new(1, 1);
    private readonly Callback _callback;
    private BluetoothGatt? _gatt;
    private BluetoothGattCharacteristic? _rx;
    private BluetoothGattCharacteristic? _tx;
    private int _mtu = 23;

    public AndroidGattConnection(Context context, BluetoothDevice device)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _callback = new Callback(this);
    }

    /// <summary>Adresse MAC du device.</summary>
    public string Address => _device.Address ?? string.Empty;

    public bool IsConnected { get; private set; }

    public int MaxWriteSize => Math.Max(20, _mtu - 3);

    /// <summary>Délai de chaque étape GATT (connexion, MTU, découverte, écriture).</summary>
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public event Action<byte[]>? NotificationReceived;
    public event Action? Disconnected;

    public async Task ConnectAsync(LinkBleProfile profile, CancellationToken cancellationToken = default)
    {
        await _gattLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connected = _callback.Expect(Callback.Op.Connect);
            _gatt = _device.ConnectGatt(_context, false, _callback, BluetoothTransports.Le)
                    ?? throw new IOException("ConnectGatt a échoué.");
            await Wait(connected, cancellationToken).ConfigureAwait(false);
            IsConnected = true;

            // MTU élevé : 244 octets utiles au lieu de 20 (non bloquant si refusé).
            var mtu = _callback.Expect(Callback.Op.Mtu);
            if (_gatt.RequestMtu(LinkBleProfile.PreferredMtu))
            {
                try { _mtu = await Wait(mtu, cancellationToken).ConfigureAwait(false); }
                catch (TimeoutException) { _mtu = 23; }
            }

            var discovered = _callback.Expect(Callback.Op.Discover);
            if (!_gatt.DiscoverServices())
                throw new IOException("DiscoverServices a échoué.");
            await Wait(discovered, cancellationToken).ConfigureAwait(false);

            var service = _gatt.GetService(Uuid(profile.ServiceUuid))
                          ?? throw new IOException($"Service {profile.Name} ({profile.ServiceUuid}) absent du device.");
            _rx = service.GetCharacteristic(Uuid(profile.RxCharacteristicUuid))
                  ?? throw new IOException("Caractéristique RX absente.");
            _tx = service.GetCharacteristic(Uuid(profile.TxCharacteristicUuid))
                  ?? throw new IOException("Caractéristique TX absente.");

            _rx.WriteType = (_rx.Properties & GattProperty.WriteNoResponse) != 0
                ? GattWriteType.NoResponse
                : GattWriteType.Default;

            // Activation des notifications (local + descripteur CCCD côté device)
            if (!_gatt.SetCharacteristicNotification(_tx, true))
                throw new IOException("Activation des notifications impossible.");
            var cccd = _tx.GetDescriptor(ClientConfigDescriptor)
                       ?? throw new IOException("Descripteur CCCD absent.");
            var enable = BluetoothGattDescriptor.EnableNotificationValue!.ToArray();
            var written = _callback.Expect(Callback.Op.Descriptor);
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                if (_gatt.WriteDescriptor(cccd, enable) != (int)CurrentBluetoothStatusCodes.Success)
                    throw new IOException("Écriture CCCD refusée.");
            }
            else
            {
#pragma warning disable CA1422 // API obsolète conservée pour Android < 13
                cccd.SetValue(enable);
                if (!_gatt.WriteDescriptor(cccd))
                    throw new IOException("Écriture CCCD refusée.");
#pragma warning restore CA1422
            }
            await Wait(written, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            CloseGatt();
            throw;
        }
        finally
        {
            _gattLock.Release();
        }
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        await _gattLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_gatt is null || _rx is null || !IsConnected)
                throw new InvalidOperationException("Connexion BLE fermée.");

            var value = data.ToArray();
            var done = _callback.Expect(Callback.Op.Write);
            bool started;
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                started = _gatt.WriteCharacteristic(_rx, value, (int)_rx.WriteType)
                          == (int)CurrentBluetoothStatusCodes.Success;
            }
            else
            {
#pragma warning disable CA1422
                _rx.SetValue(value);
                started = _gatt.WriteCharacteristic(_rx);
#pragma warning restore CA1422
            }
            if (!started)
                throw new IOException("Écriture GATT refusée (file pleine ou lien perdu).");

            // Android signale aussi la fin des écritures « sans réponse » : contrôle de flux naturel.
            await Wait(done, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gattLock.Release();
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        CloseGatt();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        CloseGatt();
        _gattLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private void CloseGatt()
    {
        var gatt = _gatt;
        _gatt = null;
        IsConnected = false;
        if (gatt is null)
            return;
        try
        {
            gatt.Disconnect();
            gatt.Close();
        }
        catch (Java.Lang.Exception)
        {
            // pile Bluetooth déjà arrêtée
        }
    }

    private async Task<int> Wait(Task<int> task, CancellationToken ct)
    {
        try
        {
            return await task.WaitAsync(OperationTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("Délai GATT dépassé.");
        }
    }

    private static UUID Uuid(Guid guid) => UUID.FromString(guid.ToString())!;

    private void OnLinkLost()
    {
        bool wasConnected = IsConnected;
        IsConnected = false;
        _callback.FailAll(new IOException("Connexion BLE perdue."));
        if (wasConnected)
            Disconnected?.Invoke();
    }

    private void OnNotification(byte[]? value)
    {
        if (value is { Length: > 0 })
            NotificationReceived?.Invoke(value);
    }

    /// <summary>Codes de statut Android 13+ (BluetoothStatusCodes).</summary>
    private enum CurrentBluetoothStatusCodes
    {
        Success = 0,
    }

    private sealed class Callback : BluetoothGattCallback
    {
        public enum Op { Connect, Mtu, Discover, Descriptor, Write }

        private readonly AndroidGattConnection _owner;
        private readonly Dictionary<Op, TaskCompletionSource<int>> _pending = new();
        private readonly object _lock = new();

        public Callback(AndroidGattConnection owner) => _owner = owner;

        public Task<int> Expect(Op op)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
                _pending[op] = tcs;
            return tcs.Task;
        }

        private void Complete(Op op, GattStatus status, int value = 0)
        {
            TaskCompletionSource<int>? tcs;
            lock (_lock)
            {
                if (!_pending.Remove(op, out tcs))
                    return;
            }
            if (status == GattStatus.Success)
                tcs.TrySetResult(value);
            else
                tcs.TrySetException(new IOException($"Opération GATT {op} : statut {status}"));
        }

        public void FailAll(Exception ex)
        {
            List<TaskCompletionSource<int>> all;
            lock (_lock)
            {
                all = _pending.Values.ToList();
                _pending.Clear();
            }
            foreach (var tcs in all)
                tcs.TrySetException(ex);
        }

        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
        {
            if (newState == ProfileState.Connected && status == GattStatus.Success)
                Complete(Op.Connect, status);
            else if (newState == ProfileState.Disconnected)
                _owner.OnLinkLost();
        }

        public override void OnMtuChanged(BluetoothGatt? gatt, int mtu, GattStatus status)
            => Complete(Op.Mtu, status, mtu);

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
            => Complete(Op.Discover, status);

        public override void OnDescriptorWrite(BluetoothGatt? gatt, BluetoothGattDescriptor? descriptor, GattStatus status)
            => Complete(Op.Descriptor, status);

        public override void OnCharacteristicWrite(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, GattStatus status)
            => Complete(Op.Write, status);

        // Android 13+
        public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value)
            => _owner.OnNotification(value);

        // Android < 13
#pragma warning disable CA1422
        public override void OnCharacteristicChanged(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic)
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(33))
                _owner.OnNotification(characteristic?.GetValue());
        }
#pragma warning restore CA1422
    }
}
