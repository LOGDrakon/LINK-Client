// Exemple LINK v2 : découverte Wi-Fi, session chiffrée, provisioning, authentification,
// commandes et évènements.
//
//   dotnet run --project examples/LINK.Example.Console.SecureV2 -- --discover
//   dotnet run --project examples/LINK.Example.Console.SecureV2 -- --tcp 127.0.0.1:5000 --password s3cret
//   dotnet run --project examples/LINK.Example.Console.SecureV2 -- --serial COM3 --password s3cret
//
// Device de test : LINK-Device/build/link_host_device (vrai firmware C compilé pour l'hôte).

using Link.Client;
using Link.Client.Extensions;
using Link.Client.Security;
using Link.Core.Transport;
using Link.Transport.Serial;
using Link.Transport.Tcp;

const string AppId = "DRAGON";

string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
var password = Arg("--password") ?? "s3cret";

ILinkTransport transport;
if (args.Contains("--discover"))
{
    Console.WriteLine("Recherche des devices LINK sur le réseau…");
    var devices = await LinkLanDiscovery.ScanAsync(extraTargets: [System.Net.IPAddress.Loopback]);
    foreach (var d in devices)
        Console.WriteLine($"  {d.AppId} « {d.Name} » {d.Address}:{d.Port} UID={d.Uid} FP={d.Fingerprint} PROTO={string.Join(',', d.Protocols)}");
    var target = devices.FirstOrDefault(d => d.AppId == AppId);
    if (target is null)
        return;
    transport = new LinkTcpTransport(target.ToTcpOptions());
}
else if (Arg("--serial") is { } port)
{
    transport = new LinkSerialTransport(new LinkSerialOptions { PortName = port });
}
else
{
    var endpoint = (Arg("--tcp") ?? "127.0.0.1:5000").Split(':');
    transport = new LinkTcpTransport(new LinkTcpOptions { Host = endpoint[0], Port = int.Parse(endpoint[1]) });
}

await using var client = new LinkClient(new LinkClientOptions { Transport = transport, CommandTimeout = TimeSpan.FromSeconds(5) });
await client.ConnectAsync();

// 1. Identification (v1, compatible avec tous les devices)
var info = await client.GetDeviceInfoAsync(AppId);
Console.WriteLine($"Device {info.Model} FW={info.Firmware} UID={info.Uid} PROTO={string.Join(',', info.Protocols)} FP={info.Fingerprint}");
if (!info.SupportsV2)
{
    Console.WriteLine("Device LINK v1 : utilisez AuthenticateAsync (legacy).");
    return;
}

// 2. Session chiffrée avec épinglage de l'identité (Trust On First Use)
var session = await client.OpenSecureSessionAsync(AppId, new LinkSecureSessionOptions
{
    TrustStore = LinkFileTrustStore.CreateDefault(),
});
Console.WriteLine($"Session chiffrée établie (empreinte {session.Fingerprint}, " +
                  $"{(session.IsFingerprintPinned ? "déjà connue" : "épinglée pour la première fois")})");

// 3. Provisioning ou authentification
if (!session.IsProvisioned)
{
    await client.SetPasswordAsync(password);
    Console.WriteLine("Device neuf : mot de passe défini.");
}
else
{
    var auth = await client.AuthenticateSecureAsync(password);
    if (!auth.Success)
    {
        Console.WriteLine($"Authentification refusée : {auth.Error} " +
                          $"(essais restants : {auth.RemainingAttempts}, réessayer dans : {auth.RetryAfter})");
        return;
    }
    Console.WriteLine("Authentifié.");
}

// 4. Commandes applicatives (chiffrées)
var dragon = client.WithAppId(AppId);
Console.WriteLine($"GETTEMP -> {string.Join(' ', (await dragon.SendAsync("GETTEMP")).ReturnArguments)}");
Console.WriteLine($"SETLED  -> {string.Join(' ', (await dragon.SendAsync("SETLED", default, "ON")).ReturnArguments)}");

// 5. Évènements non sollicités
client.EventReceived += e => Console.WriteLine($"Évènement {e.Command} {string.Join(' ', e.Arguments)} (chiffré : {e.IsEncrypted})");
var sub = await dragon.SendAsync("SUBSCRIBE");
if (sub.ReturnArguments.FirstOrDefault() == "OK")
{
    Console.WriteLine("Écoute des évènements pendant 6 s…");
    await Task.Delay(TimeSpan.FromSeconds(6));
}

// 6. Fin de session
await client.CloseSecureSessionAsync();
Console.WriteLine("Session fermée.");
