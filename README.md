# LINK-Client SDK

SDK .NET pour communiquer avec des appareils compatibles **LINK** (USB/Serial), envoyer des commandes, récupérer les informations de l’appareil et gérer l’authentification/chiffrement au niveau client.

## À propos du protocole LINK

Le protocole suit un format de trame texte terminé par `\0` :

- Trame standard : `LINK:[APP-ID]:[COMMAND]:[ARGS_0]:...:[ARGS_n]\0`
- Trame de découverte d’application : `LINK:GETAPP\0`

Commandes standard côté protocole :

- `GETAPP` : récupère l’identifiant applicatif (`APP-ID`).
- `GETV` : récupère la version/informations du device.
- `RETURN` : réponse d’un appareil à une commande.
- `AUTH` : authentification optionnelle selon le firmware.

## Contenu du repository

- `src/LINK.Core` : structures de trames, parsing, contrats de transport.
- `src/LINK.Transport.Serial` : implémentation `SerialPort`.
- `src/LINK.Client` : API haut niveau (send/receive, extensions, découverte).
- `examples/` : exemples console, WPF, WinUI.
- `tests/` : tests unitaires.

## Démarrage rapide

### 1) Créer un transport série

```csharp
using Link.Transport.Serial;

var transport = new LinkSerialTransport(new LinkSerialOptions
{
    PortName = "COM3",
    BaudRate = 115200
});
```

### 2) Créer et connecter le client

```csharp
using Link.Client;

var client = new LinkClient(new LinkClientOptions
{
    Transport = transport,
    CommandTimeout = TimeSpan.FromSeconds(2)
});

await client.ConnectAsync();
```

### 3) Travailler avec un `APP-ID`

```csharp
using Link.Client.Extensions;

var dragon = client.WithAppId("DRAGON");
var info = await dragon.GetDeviceInfoAsync();
var frame = await dragon.SendAsync("GETTEMP");
```

## Documentation

- Guide SDK complet (fonctions, modèles, découverte, sécurité, snippets) :
  - [`docs/LINK_SDK_Documentation.md`](docs/LINK_SDK_Documentation.md)
- Vue d’architecture globale LINK :
  - [`docs/LINK_Architecture.md`](docs/LINK_Architecture.md)

## Licence

Projet sous licence Apache-2.0. Voir [`LICENSE`](LICENSE).
