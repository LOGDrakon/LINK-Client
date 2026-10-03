using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Link.Transport.Ble;

namespace Link.Transport.Android;

/// <summary>Device BLE annonçant un profil LINK.</summary>
public sealed record LinkBleScanResult(BluetoothDevice Device, string? Name, int Rssi, LinkBleProfile Profile)
{
    public string Address => Device.Address ?? string.Empty;
}

/// <summary>
/// Recherche des devices LINK en Bluetooth LE (filtre sur l'UUID de service des profils).
/// Permissions : BLUETOOTH_SCAN (Android 12+) ou ACCESS_FINE_LOCATION (Android ≤ 11).
/// </summary>
public static class LinkBleScanner
{
    public static async Task<IReadOnlyList<LinkBleScanResult>> ScanAsync(
        Context context,
        TimeSpan? duration = null,
        IEnumerable<LinkBleProfile>? profiles = null,
        CancellationToken cancellationToken = default)
    {
        var manager = (BluetoothManager?)context.GetSystemService(Context.BluetoothService)
                      ?? throw new NotSupportedException("Bluetooth indisponible.");
        var adapter = manager.Adapter;
        if (adapter is null || !adapter.IsEnabled)
            throw new InvalidOperationException("Bluetooth désactivé.");
        var scanner = adapter.BluetoothLeScanner
                      ?? throw new InvalidOperationException("Scanner BLE indisponible.");

        var wanted = (profiles ?? new[] { LinkBleProfile.Link }).ToList();
        var filters = wanted
            .Select(p => new ScanFilter.Builder()
                .SetServiceUuid(ParcelUuid.FromString(p.ServiceUuid.ToString()))!
                .Build()!)
            .ToList();
        var settings = new ScanSettings.Builder()
            .SetScanMode(global::Android.Bluetooth.LE.ScanMode.LowLatency)!
            .Build()!;

        var callback = new Callback(wanted);
        scanner.StartScan(filters, settings, callback);
        try
        {
            await Task.Delay(duration ?? TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);
        }
        catch (System.OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            scanner.StopScan(callback);
        }

        if (callback.Error is { } error)
            throw new IOException($"Échec du scan BLE : {error}");
        return callback.Results.Values.OrderByDescending(r => r.Rssi).ToArray();
    }

    private sealed class Callback : ScanCallback
    {
        private readonly List<LinkBleProfile> _profiles;
        public Dictionary<string, LinkBleScanResult> Results { get; } = new();
        public ScanFailure? Error { get; private set; }

        public Callback(List<LinkBleProfile> profiles) => _profiles = profiles;

        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result)
        {
            var device = result?.Device;
            if (device?.Address is null)
                return;

            var uuids = result!.ScanRecord?.ServiceUuids?
                .Select(u => u.ToString()?.ToLowerInvariant())
                .ToHashSet() ?? new HashSet<string?>();
            var profile = _profiles.FirstOrDefault(p => uuids.Contains(p.ServiceUuid.ToString())) ?? _profiles[0];

            lock (Results)
                Results[device.Address] = new LinkBleScanResult(device, result.ScanRecord?.DeviceName ?? device.Name,
                                                                result.Rssi, profile);
        }

        public override void OnScanFailed(ScanFailure errorCode) => Error = errorCode;
    }
}
