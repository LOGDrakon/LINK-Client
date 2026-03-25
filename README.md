# LINK-Client SDK

SDK .NET pour communiquer avec des appareils compatibles **LINK** (USB/Serial), envoyer des commandes, récupérer les informations de l'appareil et gérer l'authentification/chiffrement au niveau client.

## À propos du protocole LINK

Le protocole suit un format de trame texte terminé par `\0` :

- Trame standard : `LINK:[APP-ID]:[COMMAND]:[ARGS_0]:...:[ARGS_n]\0`
- Trame de découverte d'application : `LINK:GETAPP\0`

Commandes standard côté protocole :

- `GETAPP` : récupère l'identifiant applicatif (`APP-ID`).
- `GETV` : récupère la version/informations du device.
- `RETURN` : réponse d'un appareil à une commande.
- `AUTH` : authentification par hash avec échange de nonces (challenge-response).
- `AUTH_INIT` : échange de nonces aléatoires entre client et device (précède `AUTH`).

## Contenu du repository

- `src/LINK.Core` : structures de trames, parsing, contrats de transport.
- `src/LINK.Transport.Serial` : implémentation `SerialPort`.
- `src/LINK.Transport.Tcp` : implémentation TCP client (simulateur, tests locaux).
- `src/LINK.Client` : API haut niveau (send/receive, extensions, découverte).
- `examples/` : exemples console, WPF, WinUI.
- `tests/` : tests unitaires.

## Installation via NuGet

Installez les packages depuis [NuGet.org](https://www.nuget.org/) :

```bash
# Package principal (inclut Core + Transport Serial)
dotnet add package LINK.Client

# Ou installez les composants individuellement
dotnet add package LINK.Core
dotnet add package LINK.Transport.Serial
dotnet add package LINK.Transport.Tcp
```

Ou via le Package Manager :

```powershell
Install-Package LINK.Client
```

## Démarrage rapide

### 1a) Transport série (appareil réel ou COM virtuel)

```csharp
using Link.Transport.Serial;

var transport = new LinkSerialTransport(new LinkSerialOptions
{
    PortName = "COM3",
    BaudRate = 115200
});
```

### 1b) Transport TCP (simulateur Python ou appareil réseau)

Le transport TCP est utile pour tester sans appareil physique et sans pont
COM virtuel — notamment avec le simulateur Python inclus dans le repository.

```csharp
using Link.Transport.Tcp;

var transport = new LinkTcpTransport(new LinkTcpOptions
{
    Host = "127.0.0.1",
    Port = 5000
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

## Simulateur Python TCP

Pour tester localement sans appareil matériel ni port COM virtuel, lancez le
simulateur inclus dans `examples/LINK.Device.Simulator/` :

```bash
python examples/LINK.Device.Simulator/link_tcp_simulator.py
# démarre sur 127.0.0.1:5000 par défaut

# options :
python examples/LINK.Device.Simulator/link_tcp_simulator.py \
    --host 0.0.0.0 --port 5000 \
    --app-id DRAGON --password password --temp 24.6
```

Puis lancez l'exemple TCP dédié :

```bash
dotnet run --project examples/LINK.Example.Console.Tcp
```

Ou l'exemple de base en mode TCP :

```bash
dotnet run --project examples/LINK.Example.Console.Basic -- --tcp
dotnet run --project examples/LINK.Example.Console.Basic -- --tcp 192.168.1.10 9000
```

> **Pourquoi TCP ?**  Les ports COM virtuels Windows (com0com, etc.) sont souvent
> fragiles et dépendants du matériel.  Le transport TCP fonctionne partout
> (Windows, Linux, macOS) et ne nécessite aucun driver supplémentaire.

## Générer les projets d'exemple

Build avec :
```bash
dotnet build <path to the .csproj exemple file>
```

Lancer avec :
```bash
dotnet run --project <path to the .csproj exemple file>
```

## Documentation

- Guide SDK complet (fonctions, modèles, découverte, sécurité, snippets) :
  - [`docs/LINK_SDK_Documentation.md`](docs/LINK_SDK_Documentation.md)
- Vue d'architecture globale LINK :
  - [`docs/LINK_Architecture.md`](docs/LINK_Architecture.md)

## Licence

Projet sous licence Apache-2.0. Voir [`LICENSE`](LICENSE).
