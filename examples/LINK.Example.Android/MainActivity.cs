using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Android.OS;
using Android.Views;
using Android.Widget;
using Link.Client;
using Link.Client.Extensions;
using Link.Client.Security;
using Link.Core.Transport;
using Link.Transport.Android;
using Link.Transport.Ble;
using Link.Transport.Tcp;

namespace Link.Example.Android;

/// <summary>
/// Exemple LINK sur Android : connexion BLE, USB (OTG) ou Wi-Fi, session chiffrée,
/// provisioning / authentification, lecture d'une valeur et évènements.
/// Interface construite en code pour rester sans dépendance (pas de XAML / MAUI).
/// </summary>
[Activity(Label = "LINK", MainLauncher = true, Exported = true, Theme = "@android:style/Theme.Material.Light")]
[IntentFilter(new[] { UsbManager.ActionUsbDeviceAttached })]
[MetaData(UsbManager.ActionUsbDeviceAttached, Resource = "@xml/usb_device_filter")]
public class MainActivity : Activity
{
    private const string AppId = "DRAGON";
    private const int PermissionRequest = 42;

    private TextView _log = null!;
    private EditText _password = null!;
    private LinkClient? _client;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(32, 32, 32, 32);

        _password = new EditText(this) { Hint = "Mot de passe du device" };
        _password.InputType = global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextVariationPassword;
        root.AddView(_password);

        root.AddView(Button("Bluetooth LE", async () => await ConnectBleAsync()));
        root.AddView(Button("USB (OTG)", async () => await ConnectUsbAsync()));
        root.AddView(Button("Wi-Fi (découverte)", async () => await ConnectWifiAsync()));
        root.AddView(Button("Déconnecter", async () => await DisconnectAsync()));

        _log = new TextView(this) { TextSize = 13 };
        var scroll = new ScrollView(this);
        scroll.AddView(_log);
        root.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        SetContentView(root);

        if (!LinkAndroidPermissions.HasBluetoothPermissions(this))
            RequestPermissions(LinkAndroidPermissions.Bluetooth, PermissionRequest);
    }

    private Button Button(string text, Func<Task> action)
    {
        var button = new Button(this) { Text = text };
        button.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Log($"Erreur : {ex.Message}");
            }
        };
        return button;
    }

    private void Log(string message) => RunOnUiThread(() => _log.Append(message + "\n"));

    // ---- Transports --------------------------------------------------------

    private async Task ConnectBleAsync()
    {
        if (!LinkAndroidPermissions.HasBluetoothPermissions(this))
        {
            RequestPermissions(LinkAndroidPermissions.Bluetooth, PermissionRequest);
            return;
        }
        Log("Scan BLE (4 s)…");
        var found = await LinkBleScanner.ScanAsync(this, profiles: LinkBleProfile.All);
        foreach (var d in found)
            Log($"  {d.Name ?? "?"} {d.Address} RSSI {d.Rssi} [{d.Profile.Name}]");
        var target = found.FirstOrDefault() ?? throw new InvalidOperationException("Aucun device LINK BLE.");
        await RunAsync(new LinkBleTransport(new AndroidGattConnection(this, target.Device), target.Profile));
    }

    private async Task ConnectUsbAsync()
    {
        var device = AndroidUsbSerialTransport.FindCdcDevices(this).FirstOrDefault()
                     ?? throw new InvalidOperationException("Aucun device USB CDC branché.");
        if (!await AndroidUsbSerialTransport.RequestPermissionAsync(this, device))
            throw new UnauthorizedAccessException("Accès USB refusé.");
        await RunAsync(new AndroidUsbSerialTransport(this, device));
    }

    private async Task ConnectWifiAsync()
    {
        Log("Découverte Wi-Fi…");
        var devices = await LinkLanDiscovery.ScanAsync(appIdFilter: AppId);
        var target = devices.FirstOrDefault() ?? throw new InvalidOperationException("Aucun device LINK sur le réseau.");
        Log($"  {target.Name} {target.Address}:{target.Port} FP={target.Fingerprint}");
        await RunAsync(new LinkTcpTransport(target.ToTcpOptions()));
    }

    // ---- Scénario LINK -------------------------------------------------------

    private async Task RunAsync(ILinkTransport transport)
    {
        await DisconnectAsync();
        _client = new LinkClient(new LinkClientOptions { Transport = transport, CommandTimeout = TimeSpan.FromSeconds(5) });
        _client.EventReceived += e => Log($"Évènement {e.Command} {string.Join(' ', e.Arguments)}");
        await _client.ConnectAsync();

        var info = await _client.GetDeviceInfoAsync(AppId);
        Log($"{info.Model} FW={info.Firmware} UID={info.Uid} PROTO={string.Join(',', info.Protocols)}");
        if (!info.SupportsV2)
        {
            Log("Device LINK v1 : session sécurisée indisponible.");
            return;
        }

        var session = await _client.OpenSecureSessionAsync(AppId, new LinkSecureSessionOptions
        {
            TrustStore = new LinkFileTrustStore(Path.Combine(FilesDir!.AbsolutePath, "known_devices.json")),
        });
        Log($"Session chiffrée — empreinte {session.Fingerprint}" + (session.IsFingerprintPinned ? "" : " (nouvelle, vérifiez l'étiquette)"));

        var password = _password.Text ?? string.Empty;
        if (!session.IsProvisioned)
        {
            await _client.SetPasswordAsync(password);
            Log("Device neuf : mot de passe défini.");
        }
        else
        {
            var auth = await _client.AuthenticateSecureAsync(password);
            if (!auth.Success)
            {
                Log($"Authentification refusée : {auth.Error}");
                return;
            }
            Log("Authentifié.");
        }

        var temp = await _client.SendCommandAsync(AppId, "GETTEMP");
        Log($"Température : {string.Join(' ', temp.ReturnArguments)}");
        await _client.SendCommandAsync(AppId, "SUBSCRIBE");
    }

    private async Task DisconnectAsync()
    {
        if (_client is null)
            return;
        try
        {
            await _client.CloseSecureSessionAsync();
        }
        catch (Exception)
        {
            // lien déjà perdu
        }
        await _client.DisposeAsync();
        _client = null;
        Log("Déconnecté.");
    }

    protected override void OnDestroy()
    {
        _ = DisconnectAsync();
        base.OnDestroy();
    }
}
