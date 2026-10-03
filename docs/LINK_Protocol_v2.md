# Protocole LINK v2 — Spécification

> Statut : **brouillon 2.0** — compatible ascendant avec LINK v1 (trames texte).
> Implémentations de référence :
> - Client .NET : `LOGDrakon/LINK-Client` (`LINK.Core`, `LINK.Client`, transports)
> - Device C (STM32 + port hôte POSIX) : `LOGDrakon/LINK-Device`

## 1. Objectifs

| Objectif | v1 | v2 |
|---|---|---|
| Transports | USB CDC / UART, TCP | + **BLE GATT**, **Wi-Fi (TCP + découverte UDP/mDNS)**, Android (USB host, BLE) |
| Trames | texte `\x1f` / `\0`, non binaire-safe | binaire, **COBS**, longueur explicite, **CRC-16** |
| Corrélation requête/réponse | `APP-ID + commande` (1 seule commande identique en vol) | **numéro de séquence** 16 bits |
| Authentification | `H(nc + nd + mot_de_passe)` → le device stocke le mot de passe **en clair** ; SHA-1 accepté | Vérificateur **PBKDF2-SHA256** salé stocké sur le device, preuve HMAC liée à la session |
| Changement de mot de passe | `CHPWD` + CRC32 : le nouveau hash est inutilisable par le device (lié aux nonces) | `CHPWD` chiffré, preuve de l'ancien mot de passe, nouveau vérificateur |
| Confidentialité / intégrité | aucune (`ENC=AES128` jamais implémenté) | **ECDH P-256 éphémère + AES-128-GCM**, compteur anti-rejeu, secret persistant (*forward secrecy*) |
| Authentification du device | aucune (MITM trivial) | **clé d'identité ECDSA P-256** du device, épinglage (TOFU / empreinte) |
| Force brute | illimitée | temporisation exponentielle persistée en flash |
| Évènements device → client | non | trames `EVENT` non sollicitées |

## 2. Couches

```
┌──────────────────────────────────────────────┐
│ Application  (GETTEMP, SETLED, …)            │
├──────────────────────────────────────────────┤
│ Commandes standard (GETV, HELLO, AUTH, …)    │
├──────────────────────────────────────────────┤
│ Session sécurisée (AES-128-GCM, anti-rejeu)  │  optionnelle
├──────────────────────────────────────────────┤
│ Paquet v2 (en-tête, séquence, CRC-16)        │
├──────────────────────────────────────────────┤
│ Tramage : COBS + délimiteur 0x00             │  identique sur tous les flux
├──────────────────────────────────────────────┤
│ USB CDC │ UART │ TCP (Wi-Fi/Ethernet) │ BLE  │
└──────────────────────────────────────────────┘
```

Tous les transports sont traités comme un **flux d'octets**. Le délimiteur de
trame est toujours `0x00`, ce qui permet de faire cohabiter v1 et v2 sur le
même lien.

## 3. Tramage

### 3.1 Détection de version

Un récepteur accumule les octets jusqu'à `0x00`, puis :

- si le bloc commence par `LINK\x1f` (`4C 49 4E 4B 1F`) → **trame v1** (texte) ;
- sinon → décodage **COBS** ; si le premier octet décodé vaut `0xB2` → **paquet v2** ;
- sinon → la trame est ignorée.

Un device répond **dans la version de la requête reçue**. Les trames v1 restent
autorisées pour `GETAPP`, `GETV`, `PING` et `DISCOVER` (découverte), et pour les
commandes applicatives tant que le device n'est **pas provisionné** (pas de mot de passe).

Taille maximale d'une trame (avant COBS) : `LINK_MAX_FRAME` = 1024 octets par
défaut. Tout bloc plus long est rejeté et le buffer de réception est purgé
(protection contre l'épuisement mémoire).

### 3.2 Paquet v2 (avant encodage COBS)

| Offset | Taille | Champ | Description |
|---|---|---|---|
| 0 | 1 | `magic` | `0xB2` |
| 1 | 1 | `flags` | bit0 `ENC` (corps chiffré), bit1 `RESP` (réponse), bit2 `EVENT` (non sollicité), bits 3-7 = 0 |
| 2 | 2 | `seq` | numéro de séquence, little-endian. La réponse reprend le `seq` de la requête. `0` réservé aux évènements |
| 4 | 2 | `len` | longueur du corps, little-endian |
| 6 | `len` | `body` | corps (clair ou chiffré) |
| 6+len | 2 | `crc` | CRC-16/CCITT-FALSE (poly `0x1021`, init `0xFFFF`) des octets `[0, 6+len)`, little-endian |

Le CRC détecte les erreurs de ligne (UART bruité) ; il **n'est pas** une
protection cryptographique — c'est le rôle du tag GCM.

### 3.3 Corps en clair

Suite de champs `varint(longueur) || octets` (varint = LEB128 non signé) :

```
champ 0 : APP-ID       (UTF-8, peut être vide pour GETAPP / DISCOVER)
champ 1 : COMMANDE     (ASCII majuscules)
champ 2…: ARGUMENTS    (octets quelconques ; UTF-8 par convention, hex pour le binaire)
```

Une réponse utilise la commande `RETURN` et place la commande d'origine en
premier argument, exactement comme en v1 :
`APP-ID, "RETURN", "GETV", "LINKv2.0", "UID=…", …`

### 3.4 Corps chiffré (`flags.ENC = 1`)

```
counter (4 octets LE) || ciphertext || tag (16 octets)
```

- Algorithme : **AES-128-GCM**.
- `nonce (12 o)` = `iv_dir XOR (0x00…00 || counter en big-endian sur les 4 derniers octets)`.
- `AAD` = octets d'en-tête `[0, 6)` || `counter` (4 octets LE).
- Le texte clair est un corps en clair (§3.3).
- Chaque émetteur démarre à `counter = 1` et l'incrémente de 1 par trame.
  Le récepteur **rejette** toute trame dont le compteur n'est pas strictement
  supérieur au dernier compteur accepté (anti-rejeu), ou dont le tag est invalide.
- À `counter = 0xFFFFFFFF`, la session doit être renégociée (`HELLO`).

## 4. Session sécurisée

### 4.1 Identité du device

Au premier démarrage, le device génère une paire de clés **ECDSA P-256**
(`ds_priv`, `ds_pub`) stockée en flash. L'**empreinte** du device est :

```
FP = hex( SHA-256(ds_pub)[0..8] )   // 16 caractères, ex. "a1b2c3d4e5f60718"
```

Elle peut être imprimée sur l'étiquette / un QR code, affichée sur un écran, ou
retournée par `DISCOVER`. Le client l'épingle (*Trust On First Use* par défaut,
ou mode strict : empreinte fournie à l'avance).

### 4.2 Poignée de main `HELLO`

Requête (paquet v2 en clair) :

```
APP-ID, "HELLO", "2", hex(ce_pub 65 o), hex(cn 16 o)
```

`ce_pub` : clé publique éphémère du client (P-256, non compressée `04||X||Y`),
`cn` : nonce aléatoire.

Réponse :

```
APP-ID, "RETURN", "HELLO", "2", hex(de_pub), hex(dn), hex(ds_pub),
        "PBKDF2-SHA256", hex(salt 16 o), "<itérations>", "<prov 0|1>", hex(sig 64 o)
```

Transcript :

```
TH = SHA-256( "LINKv2/HS"
            || u8(len(APP-ID)) || APP-ID
            || ce_pub || cn || de_pub || dn || ds_pub
            || u8(len(kdf)) || kdf
            || salt || u32be(itérations) || u8(prov) )
sig = ECDSA-P256(ds_priv, TH)            // format IEEE-P1363 r||s
```

Le client vérifie `sig` avec `ds_pub` **et** que `ds_pub` correspond à l'empreinte
épinglée. Échec ⇒ abandon (possible attaque MITM).

Dérivation des clés :

```
Z   = ECDH(ce_priv, de_pub)                              // 32 o (coordonnée X)
OKM = HKDF-SHA256(ikm = Z, salt = TH, info = "LINKv2 session keys", L = 56)
k_c2d = OKM[0..16)   k_d2c = OKM[16..32)   iv_c2d = OKM[32..44)   iv_d2c = OKM[44..56)
```

Après `HELLO`, **toutes** les trames du canal sont chiffrées. Une trame en clair
reçue sur un canal sécurisé est ignorée, sauf un nouveau `HELLO` (qui remplace
la session). Une session est **par canal** (un client USB et un client BLE ont
chacun la leur).

### 4.3 Authentification `AUTH`

```
K_pwd = PBKDF2-HMAC-SHA256(UTF-8(mot_de_passe), salt, itérations, 32)
proof = HMAC-SHA256(K_pwd, "LINKv2/AUTH" || TH)
→  APP-ID, "AUTH", hex(proof)          (chiffré)
```

Le device ne stocke que `salt`, `itérations` et `K_pwd` (jamais le mot de passe).
Le calcul coûteux PBKDF2 est fait **côté client** ; le device ne fait qu'un HMAC.

Réponses : `OK` · `ERR BAD_PWD <essais_restants_avant_délai>` ·
`ERR LOCKED <secondes>` · `ERR NOT_PROVISIONED` · `ERR NO_SESSION`.

**Anti force brute** : le compteur d'échecs est persisté. Après
`LINK_AUTH_FREE_ATTEMPTS` (3) échecs, chaque nouvel essai impose un délai
`2^(échecs-3)` s (plafonné à 1 h). Le délai repart de zéro au démarrage
(on ne peut donc pas le contourner par coupure d'alimentation). Une
authentification réussie remet le compteur à zéro.

### 4.4 Provisioning `SETPWD`

Un device neuf n'a pas de mot de passe (`PROV=0`). Le premier client définit le
mot de passe **dans une session chiffrée** :

```
APP-ID, "SETPWD", hex(salt 16 o), "<itérations>", hex(K_pwd 32 o)
```

Réponses : `OK` (la session devient authentifiée) · `ERR ALREADY_PROVISIONED` ·
`ERR WEAK_KDF` (itérations < `LINK_MIN_KDF_ITERATIONS`, 10 000 par défaut).

### 4.5 Changement de mot de passe `CHPWD`

Session authentifiée obligatoire.

```
old_proof = HMAC-SHA256(K_old, "LINKv2/CHPWD" || TH)
→  APP-ID, "CHPWD", hex(old_proof), hex(new_salt), "<itérations>", hex(K_new)
```

Réponses : `OK` · `ERR BAD_OLD_PWD` · `ERR NOT_AUTH` · `ERR WEAK_KDF`.
L'ancienne preuve protège contre un poste client laissé ouvert.

### 4.6 Fin de session / réinitialisation

- `DONE` : le device répond `OK` (chiffré) puis détruit les clés de session et
  reverrouille le canal.
- `FRESET` (authentifié) : efface le mot de passe (`PROV=0`), remet le compteur
  d'échecs à zéro. `FRESET IDENTITY` régénère aussi la clé d'identité (les clients
  devront ré-épingler). Prévoir aussi un reset matériel (bouton maintenu au boot).

## 5. Commandes standard

| Commande | Clair autorisé | Auth requise | Réponse |
|---|---|---|---|
| `GETAPP` | oui (sans APP-ID) | non | `<APP-ID>` |
| `DISCOVER` | oui (sans APP-ID, UDP) | non | voir §7 |
| `GETV` | oui | non | voir §5.1 |
| `PING` | oui | non | `PONG` |
| `HELLO` | oui (v2) | non | §4.2 |
| `AUTH` | **non** | non | §4.3 |
| `SETPWD` | **non** | non (device non provisionné) | §4.4 |
| `CHPWD` | **non** | oui | §4.5 |
| `DONE` | — | non | `OK` |
| `FRESET` | **non** | oui | `OK` |
| *applicatives* | si non provisionné | si provisionné | définies par l'application |

Erreurs génériques : `ERR UNKNOWN_COMMAND`, `ERR NOT_AUTH`, `ERR NO_SESSION`,
`ERR BAD_ARGS`, `ERR BUSY`.

### 5.1 `GETV`

```
"LINKv2.0", "UID=…", "MODEL=…", "FW=1.0.0", "ENC=AES128GCM", "HASH=NONE",
"LOCKED=true|false", "PROTO=1,2", "AUTH=PBKDF2-SHA256", "PROV=0|1",
"FP=<empreinte>", "TRANSPORTS=USB,UART,BLE,WIFI", "MAXFRAME=1024"
```

- `HASH=NONE` : l'authentification v1 (`AUTH_INIT`/`AUTH` en clair) n'est plus
  proposée, car elle impose de stocker le mot de passe en clair.
- `LOCKED` : `true` si le device est provisionné et que **ce canal** n'est pas authentifié.
- Les clients v1 ignorent les clés inconnues : la compatibilité est conservée.

## 6. Transport BLE (GATT)

| Élément | UUID |
|---|---|
| Service LINK | `6c696e6b-0200-4c4b-a000-000000000001` |
| RX (client → device, *Write Without Response*) | `6c696e6b-0200-4c4b-a000-000000000002` |
| TX (device → client, *Notify*) | `6c696e6b-0200-4c4b-a000-000000000003` |

- Le contenu des caractéristiques est le **même flux d'octets** que sur UART
  (trames COBS + `0x00`), découpé en morceaux de `MTU − 3` octets.
- Le client demande un MTU de 247 (BLE 4.2+) ; à défaut 20 octets utiles.
- Publicité : UUID du service LINK + nom `LINK-<APP-ID>`.
- Profils alternatifs supportés côté client pour les modules « UART transparents » :
  **Nordic UART Service** (`6e400001-…`) et **HM-10** (`ffe0`/`ffe1`).
- La sécurité repose sur la session LINK (§4), pas sur l'appairage BLE
  (qui reste recommandé en complément : *LE Secure Connections*).

## 7. Wi-Fi / réseau local

- **Transport** : TCP, port par défaut **5000**, même flux d'octets.
- **Découverte UDP** : le client envoie en broadcast (`255.255.255.255`) et/ou
  multicast (`239.255.76.75`) sur le port **47800** :
  `LINK\x1fDISCOVER\0`. Chaque device répond en unicast :

  ```
  LINK\x1f<APP-ID>\x1fRETURN\x1fDISCOVER\x1fUID=…\x1fMODEL=…\x1fNAME=…\x1fPORT=5000\x1fPROTO=1,2\x1fFP=<empreinte>\0
  ```
- **mDNS / DNS-SD** (recommandé si la pile réseau le permet, ex. ESP-AT `AT+MDNS`) :
  service `_link._tcp.local`, TXT `app=<APP-ID>`, `uid=…`, `fp=…`.
- La session LINK chiffre le trafic : TLS n'est pas nécessaire sur le LAN.

## 8. Migration v1 → v2

1. Les devices v2 répondent toujours à `GETAPP`/`GETV`/`PING` en v1 : les
   outils de découverte existants continuent de fonctionner.
2. Un client v2 lit `PROTO=` dans `GETV` ; si `2` est présent, il bascule le
   transport en format binaire et ouvre une session (`HELLO`).
3. Les devices v1 (sans `PROTO=`) restent pilotables avec l'API v1 du SDK
   (`AuthenticateAsync`, `ChangePasswordAsync`…), marquée *legacy*.
4. Les mots de passe des devices v1 ne peuvent pas être migrés automatiquement :
   un device v2 démarre non provisionné (ou avec un vérificateur injecté en usine).

## 9. Vecteurs de test

- CRC-16/CCITT-FALSE(`"123456789"`) = `0x29B1`.
- COBS(`11 22 00 33`) = `03 11 22 02 33`.
- Paquet v2 `PING` en clair, `seq=1`, APP-ID `DRAGON` :
  corps = `06 44 52 41 47 4F 4E 04 50 49 4E 47`
  (voir les tests unitaires des deux dépôts pour les octets complets).

## 10. Modèle de menace (résumé)

| Attaque | Contre-mesure |
|---|---|
| Écoute passive (BLE, Wi-Fi) | AES-GCM, clés éphémères ECDH (*forward secrecy*) |
| Rejeu de trames | compteur strictement croissant + clés par session |
| Usurpation du device / MITM | signature ECDSA du transcript + épinglage de `ds_pub` |
| Vol du mot de passe par lecture flash | seul `K_pwd` (PBKDF2) est stocké ; activer **RDP niveau 1/2** sur STM32 |
| Force brute en ligne | délai exponentiel persistant |
| Force brute hors-ligne | impossible sans MITM réussi (la preuve circule chiffrée) |
| Déni de service mémoire | taille de trame bornée, buffers statiques |
| Rétrogradation vers v1 | un device provisionné refuse les commandes applicatives en v1 |

Limite connue : au tout premier contact en mode TOFU, un attaquant actif peut se
faire passer pour le device. Vérifiez l'empreinte (`FP`) via un canal
indépendant (étiquette, écran, QR code) pour les déploiements sensibles.
