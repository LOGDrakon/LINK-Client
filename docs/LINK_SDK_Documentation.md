# Documentation SDK LINK-Client

Ce document décrit les principales API exposées par le SDK, comment les utiliser, et les patterns recommandés pour intégrer un appareil LINK dans une application .NET.

## 1. Prérequis

- .NET 8+
- Un appareil compatible LINK connecté (souvent via port série)
- Connaître l’`APP-ID` attendu côté firmware (ex: `DRAGON`)

---

## 2. Concepts clés

### Trame LINK (`LinkFrame`)

Une trame représente un message protocolaire :

- `AppId` : application ciblée (nullable pour `GETAPP`)
- `Command` : commande (`GETV`, `RETURN`, `AUTH`, commande custom, etc.)
- `Arguments` : liste des arguments texte

Propriétés utiles :

- `IsReturn` : `true` si la commande est `RETURN`
- `ReturnedCommand` : commande d’origine concernée par le `RETURN`
- `ReturnArguments` : arguments de la réponse (sans la commande retournée)

### Transport (`ILinkTransport`)

Contrat de communication bas niveau :

- `OpenAsync()` / `CloseAsync()`
- `SendAsync(LinkFrame)`
- événement `FrameReceived`
- propriété `IsOpen`

### Client haut niveau (`LinkClient`)

`LinkClient` gère :

- la connexion au transport,
- l’envoi de commande via `SendCommandAsync(appId, command, ...)`,
- la corrélation automatique des réponses `RETURN`,
- la gestion du timeout de commande (`LinkClientOptions.CommandTimeout`).

---

## 3. API principales

## 3.1 Instanciation et connexion

```csharp
using Link.Client;
using Link.Transport.Serial;

var transport = new LinkSerialTransport(new LinkSerialOptions
{
    PortName = "COM3",
    BaudRate = 115200,
    DataBits = 8,
    Parity = System.IO.Ports.Parity.None,
    StopBits = System.IO.Ports.StopBits.One
});

var client = new LinkClient(new LinkClientOptions
{
    Transport = transport,
    CommandTimeout = TimeSpan.FromSeconds(2)
});

await client.ConnectAsync();
```

## 3.2 Envoyer une commande brute

```csharp
using Link.Core.Frames;

LinkFrame response = await client.SendCommandAsync(
    appId: "DRAGON",
    command: "GETTEMP",
    ct: CancellationToken.None);

Console.WriteLine(response.Command); // RETURN attendu
Console.WriteLine(string.Join(", ", response.ReturnArguments));
```

## 3.3 Utilisation orientée appareil (`WithAppId`)

```csharp
using Link.Client.Extensions;

var dragon = client.WithAppId("DRAGON");

var info = await dragon.GetDeviceInfoAsync();
Console.WriteLine($"App: {info.AppId}, Version: {info.Version}");

var temp = await dragon.SendAsync("GETTEMP");
Console.WriteLine(temp.ToString());
```

---

## 4. Extensions SDK (fonctions prêtes à l’emploi)

### `GetDeviceInfoAsync`

Lit la réponse `GETV` et la mappe vers `LinkDeviceInfo` :

- `Version`
- `Uid`
- `Model`
- `IsLocked`
- `EncryptionMode`

```csharp
var info = await client.GetDeviceInfoAsync("DRAGON");
```

### `AuthenticateAsync`

Envoie `AUTH` et retourne `LinkSecurityState`.

```csharp
var state = await client.AuthenticateAsync("DRAGON", "1234");
Console.WriteLine(state.IsAuthenticated ? "OK" : "ERR");
```

### `NegotiateEncryptionAsync`

Sélectionne un provider de chiffrement à partir du mode exposé par le device.

```csharp
using Link.Client.Crypto;

ILinkCryptoProvider crypto = await client.NegotiateEncryptionAsync(
    "DRAGON",
    info,
    mode => mode switch
    {
        "NONE" => new NullCryptoProvider(),
        // "AES128" => new YourAes128Provider(...),
        _ => null
    });
```

> Note: un provider AES concret reste à implémenter côté application si vous utilisez un mode de chiffrement non-`NONE`.

---

## 5. Découverte d’appareils

### Scan ponctuel

```csharp
using Link.Client.Extensions;
using Link.Transport.Serial;

var devices = await DiscoveryExtensions.ScanForLinkDevicesAsync(
    port => new LinkSerialTransport(new LinkSerialOptions
    {
        PortName = port,
        BaudRate = 115200
    }),
    timeout: TimeSpan.FromMilliseconds(800));

foreach (var d in devices)
    Console.WriteLine($"{d.PortName} | {d.AppId} | {d.DeviceInfo.Model}");
```

### Watcher temps réel (ajout/retrait de ports)

```csharp
using Link.Client.Discovery;
using Link.Transport.Serial;

var watcher = new LinkDeviceWatcher(
    port => new LinkSerialTransport(new LinkSerialOptions
    {
        PortName = port,
        BaudRate = 115200
    }),
    timeout: TimeSpan.FromMilliseconds(800),
    appIdFilter: "DRAGON");

watcher.DeviceAdded += d => Console.WriteLine($"[+] {d.PortName}");
watcher.DeviceRemoved += d => Console.WriteLine($"[-] {d.PortName}");

watcher.Start();
```

---

## 6. Gestion des erreurs et bonnes pratiques

- Prévoir un `try/catch` autour des commandes critiques (`TimeoutException`, `InvalidOperationException`).
- Utiliser un `CommandTimeout` adapté au firmware.
- Vérifier `info.IsLocked` avant d’envoyer des commandes sensibles, puis appeler `AuthenticateAsync`.
- Fermer/libérer le transport via `await client.DisposeAsync()` en fin de cycle.
- Garder les commandes/protocoles custom versionnés côté firmware (compatibilité). 

Exemple de squelette robuste :

```csharp
try
{
    await client.ConnectAsync(ct);

    var info = await client.GetDeviceInfoAsync("DRAGON", ct);

    if (info.IsLocked)
        await client.AuthenticateAsync("DRAGON", "1234");

    var resp = await client.SendCommandAsync("DRAGON", "PING", ct);
    Console.WriteLine(resp.ToString());
}
catch (TimeoutException ex)
{
    Console.WriteLine($"Timeout: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"Erreur LINK: {ex.Message}");
}
finally
{
    await client.DisposeAsync();
}
```

---

## 7. Référence rapide des objets

- `LinkClientOptions` : configuration du client (`Transport`, `CommandTimeout`).
- `LinkSerialOptions` : configuration port COM (`PortName`, `BaudRate`, etc.).
- `LinkDeviceClient` : wrapper orienté `APP-ID` (`SendAsync`, `GetDeviceInfoAsync`, `AuthenticateAsync`).
- `LinkDeviceInfo` : infos issues de `GETV`.
- `LinkSecurityState` : état d’authentification/verrouillage.
- `ILinkCryptoProvider` : contrat de chiffrement custom.

---

## 8. Où aller ensuite

- Voir les exemples prêts à lancer dans `examples/`.
- Adapter les commandes custom (`GETTEMP`, `SETCFG`, etc.) selon votre firmware.
- Pour la vision d’ensemble du protocole et des packages, consulter `docs/LINK_Architecture.md`.
