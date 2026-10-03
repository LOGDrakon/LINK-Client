using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Link.Core.Framing;
using Link.Core.Transport;

namespace Link.Transport.Android;

/// <summary>
/// Transport USB host Android pour les devices CDC-ACM (STM32 « Virtual COM Port »,
/// ESP32-S2/S3, RP2040…) branchés en USB-OTG.
/// Pour les ponts FTDI / CP210x / CH340, utilisez un pilote dédié
/// (ex. usb-serial-for-android) et dérivez de <see cref="LinkByteTransportBase"/>.
/// </summary>
public sealed class AndroidUsbSerialTransport : LinkByteTransportBase
{
    private const int UsbClassCdc = 0x02;
    private const int UsbClassCdcData = 0x0A;
    private const int SetLineCoding = 0x20;
    private const int SetControlLineState = 0x22;
    private const int RequestTypeClassInterfaceOut = 0x21;

    private readonly UsbManager _manager;
    private readonly UsbDevice _device;
    private readonly int _baudRate;
    private UsbDeviceConnection? _connection;
    private UsbInterface? _control;
    private UsbInterface? _data;
    private UsbEndpoint? _in;
    private UsbEndpoint? _out;
    private CancellationTokenSource? _readCts;
    private Thread? _readThread;

    public AndroidUsbSerialTransport(Context context, UsbDevice device, int baudRate = 115200,
                                     LinkWireFormat wireFormat = LinkWireFormat.V1Text)
        : base(maxPacketSize: 64)
    {
        _manager = (UsbManager?)context.GetSystemService(Context.UsbService)
                   ?? throw new NotSupportedException("USB host indisponible.");
        _device = device;
        _baudRate = baudRate;
        WireFormat = wireFormat;
    }

    public override bool IsOpen => _connection is not null;

    /// <summary>Liste les devices USB exposant une interface CDC-ACM.</summary>
    public static IReadOnlyList<UsbDevice> FindCdcDevices(Context context)
    {
        var manager = (UsbManager?)context.GetSystemService(Context.UsbService);
        if (manager?.DeviceList is null)
            return Array.Empty<UsbDevice>();
        return manager.DeviceList.Values
            .Where(d => Enumerable.Range(0, d.InterfaceCount)
                .Any(i => (int)d.GetInterface(i).InterfaceClass == UsbClassCdcData))
            .ToArray();
    }

    /// <summary>Demande l'autorisation d'accès au device (boîte de dialogue système).</summary>
    public static Task<bool> RequestPermissionAsync(Context context, UsbDevice device)
    {
        var manager = (UsbManager)context.GetSystemService(Context.UsbService)!;
        if (manager.HasPermission(device))
            return Task.FromResult(true);

        const string action = "link.usb.PERMISSION";
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new PermissionReceiver(tcs);
        var filter = new IntentFilter(action);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            context.RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
        else
            context.RegisterReceiver(receiver, filter);

        var flags = OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0;
        var intent = new Intent(action).SetPackage(context.PackageName);
        manager.RequestPermission(device, PendingIntent.GetBroadcast(context, 0, intent, flags));

        return tcs.Task.ContinueWith(t =>
        {
            context.UnregisterReceiver(receiver);
            return t.Result;
        }, TaskScheduler.Default);
    }

    protected override Task OpenCoreAsync(CancellationToken cancellationToken)
    {
        if (!_manager.HasPermission(_device))
            throw new UnauthorizedAccessException("Permission USB non accordée (RequestPermissionAsync).");

        for (int i = 0; i < _device.InterfaceCount; i++)
        {
            var itf = _device.GetInterface(i);
            if ((int)itf.InterfaceClass == UsbClassCdc)
                _control ??= itf;
            else if ((int)itf.InterfaceClass == UsbClassCdcData)
                _data ??= itf;
        }
        if (_data is null)
            throw new IOException("Interface CDC Data introuvable.");

        for (int i = 0; i < _data.EndpointCount; i++)
        {
            var ep = _data.GetEndpoint(i)!;
            if (ep.Type != UsbAddressing.XferBulk)
                continue;
            if (ep.Direction == UsbAddressing.In)
                _in = ep;
            else
                _out = ep;
        }
        if (_in is null || _out is null)
            throw new IOException("Points de terminaison bulk introuvables.");

        _connection = _manager.OpenDevice(_device) ?? throw new IOException("Ouverture USB impossible.");
        if (_control is not null)
            _connection.ClaimInterface(_control, true);
        if (!_connection.ClaimInterface(_data, true))
            throw new IOException("Interface USB déjà utilisée.");

        if (_control is not null)
        {
            int index = _control.Id;
            // 115200 8N1 puis DTR|RTS : certains firmwares n'émettent qu'avec DTR levé.
            var coding = new byte[]
            {
                (byte)_baudRate, (byte)(_baudRate >> 8), (byte)(_baudRate >> 16), (byte)(_baudRate >> 24),
                0, 0, 8,
            };
            _connection.ControlTransfer((UsbAddressing)RequestTypeClassInterfaceOut, SetLineCoding, 0, index, coding, coding.Length, 1000);
            _connection.ControlTransfer((UsbAddressing)RequestTypeClassInterfaceOut, SetControlLineState, 0x03, index, null, 0, 1000);
        }

        MaxPacketSize = _out.MaxPacketSize > 0 ? _out.MaxPacketSize : 64;
        _readCts = new CancellationTokenSource();
        var token = _readCts.Token;
        _readThread = new Thread(() => ReadLoop(token)) { IsBackground = true, Name = "LINK USB RX" };
        _readThread.Start();
        return Task.CompletedTask;
    }

    private void ReadLoop(CancellationToken ct)
    {
        var buffer = new byte[Math.Max(64, _in!.MaxPacketSize) * 8];
        while (!ct.IsCancellationRequested)
        {
            var connection = _connection;
            if (connection is null)
                break;
            int n = connection.BulkTransfer(_in, buffer, buffer.Length, 200);
            if (n > 0)
                OnBytesReceived(buffer.AsSpan(0, n));
            else if (n < 0 && !_manager.DeviceList!.ContainsKey(_device.DeviceName))
            {
                OnTransportError(new IOException("Device USB débranché."));
                break;
            }
        }
    }

    protected override ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var connection = _connection ?? throw new InvalidOperationException("USB fermé.");
        var bytes = data.ToArray();
        int n = connection.BulkTransfer(_out, bytes, bytes.Length, 1000);
        if (n != bytes.Length)
            throw new IOException($"Écriture USB incomplète ({n}/{bytes.Length}).");
        return ValueTask.CompletedTask;
    }

    protected override Task CloseCoreAsync(CancellationToken cancellationToken)
    {
        _readCts?.Cancel();
        _readThread?.Join(500);
        _readCts?.Dispose();
        _readCts = null;
        _readThread = null;

        var connection = _connection;
        _connection = null;
        if (connection is not null)
        {
            if (_data is not null)
                connection.ReleaseInterface(_data);
            if (_control is not null)
                connection.ReleaseInterface(_control);
            connection.Close();
        }
        return Task.CompletedTask;
    }

    private sealed class PermissionReceiver : BroadcastReceiver
    {
        private readonly TaskCompletionSource<bool> _tcs;
        public PermissionReceiver(TaskCompletionSource<bool> tcs) => _tcs = tcs;

        public override void OnReceive(Context? context, Intent? intent)
            => _tcs.TrySetResult(intent?.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false) ?? false);
    }
}
