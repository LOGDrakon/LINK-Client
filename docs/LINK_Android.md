# LINK sur Android

Le SDK LINK fonctionne sur Android via **.NET for Android** (`net8.0-android`,
utilisable aussi depuis une application **.NET MAUI**).

| Package | Rôle |
|---|---|
| `LINK.Core`, `LINK.Client` | protocole, session sécurisée (identiques au bureau) |
| `LINK.Transport.Tcp` | Wi-Fi : TCP + découverte UDP (`LinkLanDiscovery`) |
| `LINK.Transport.Ble` | transport BLE générique (`LinkBleTransport`, profils) |
| `LINK.Transport.Android` | `AndroidGattConnection` (BluetoothGatt), `LinkBleScanner`, `AndroidUsbSerialTransport` (USB host CDC-ACM), `LinkAndroidPermissions` |

## Bluetooth LE

```csharp
if (!LinkAndroidPermissions.HasBluetoothPermissions(activity))
    activity.RequestPermissions(LinkAndroidPermissions.Bluetooth, 42);

var found = await LinkBleScanner.ScanAsync(activity, profiles: LinkBleProfile.All);
var target = found.First();
var transport = new LinkBleTransport(new AndroidGattConnection(activity, target.Device), target.Profile);

var client = new LinkClient(new LinkClientOptions { Transport = transport });
await client.ConnectAsync();                                // connexion GATT, MTU 247, notifications
var session = await client.OpenSecureSessionAsync("DRAGON", new LinkSecureSessionOptions
{
    TrustStore = new LinkFileTrustStore(Path.Combine(activity.FilesDir!.AbsolutePath, "known_devices.json")),
});
```

- Les opérations GATT sont sérialisées (Android n'en accepte qu'une à la fois).
- Le MTU 247 est demandé (244 octets utiles par écriture) ; à défaut 20 octets.
- API 33+ : nouvelles signatures `WriteCharacteristic` / `OnCharacteristicChanged` utilisées automatiquement.

## USB (OTG)

```csharp
var device = AndroidUsbSerialTransport.FindCdcDevices(activity).First();
if (await AndroidUsbSerialTransport.RequestPermissionAsync(activity, device))
{
    var transport = new AndroidUsbSerialTransport(activity, device);
    ...
}
```

Supporte les devices **CDC-ACM** (STM32 Virtual COM Port, ESP32-S2/S3, RP2040).
Pour FTDI / CP210x / CH340, implémentez un transport dérivé de
`LinkByteTransportBase` avec un pilote dédié.

## Wi-Fi

`LinkLanDiscovery.ScanAsync()` puis `LinkTcpTransport` — aucune API spécifique Android.
Permission `INTERNET` requise.

## Manifeste

Voir `examples/LINK.Example.Android/AndroidManifest.xml` :
`BLUETOOTH_SCAN` (`neverForLocation`), `BLUETOOTH_CONNECT`, anciennes permissions
Bluetooth/localisation limitées à `maxSdkVersion=30`, `INTERNET`, et le filtre
`usb_device_filter.xml` pour ouvrir l'appli au branchement du device.

## Construire l'exemple

```bash
dotnet workload install android
dotnet build examples/LINK.Example.Android -t:Run   # téléphone en débogage USB
```

> Le code a été vérifié en compilation contre les assemblies de référence
> Android API 34 ; il doit encore être validé sur téléphone réel.
