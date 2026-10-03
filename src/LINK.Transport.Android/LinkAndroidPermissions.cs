using Android;
using Android.Content;
using Android.Content.PM;

namespace Link.Transport.Android;

/// <summary>
/// Permissions d'exécution nécessaires au BLE selon la version d'Android.
/// À déclarer aussi dans AndroidManifest.xml de l'application (voir l'exemple).
/// </summary>
public static class LinkAndroidPermissions
{
    public static string[] Bluetooth =>
        OperatingSystem.IsAndroidVersionAtLeast(31)
            ? new[] { Manifest.Permission.BluetoothScan, Manifest.Permission.BluetoothConnect }
            : new[] { Manifest.Permission.AccessFineLocation };

    public static bool HasBluetoothPermissions(Context context)
        => Bluetooth.All(p => context.CheckSelfPermission(p) == Permission.Granted);
}
