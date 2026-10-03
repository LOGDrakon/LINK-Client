# LINK — propositions et feuille de route

Ce document liste les évolutions **déjà réalisées** dans la mise à jour 2.0 et
les **idées proposées** pour la suite, classées par priorité.

## Réalisé (2.0)

| Domaine | Changement |
|---|---|
| Protocole | Trames v2 binaires (COBS, séquence, CRC-16), évènements non sollicités, rétro-compatibilité v1 |
| Sécurité | Session ECDH P-256 + AES-128-GCM, identité ECDSA du device, épinglage TOFU, vérificateur PBKDF2 (plus de mot de passe en clair dans la flash), anti-rejeu, anti force brute persistant, `SETPWD` / `CHPWD` / `FRESET` |
| Robustesse | Taille de trame bornée (client et device), arguments vides conservés, CRC, corrélation par séquence (plusieurs commandes identiques en parallèle) |
| Transports | BLE (profils LINK, Nordic UART, HM-10), découverte Wi-Fi UDP, série multiplateforme, Android (branche dédiée) |
| Device | Pile C99 STM32 complète + simulateur hôte basé sur le vrai code + client Python indépendant |
| Qualité | Tests corrigés (séparateur `\x1f`, échappements C#), tests v2, faux device C#, CI .NET + CI device, interop .NET ↔ C |
| Correctifs | `Aes128CryptoProvider` implémenté (AES-GCM) au lieu de lever `NotImplementedException` ; `OsPortWatcher` ne plante plus hors Windows |

## Proposé — priorité haute

1. **Mise à jour firmware (OTA / DFU) sécurisée** — `UpdateHelper` est encore vide.
   Proposition : commandes `FWINFO`, `FWBEGIN <taille> <sha256>`, `FWDATA <offset> <bloc>`,
   `FWEND`, image **signée** (ECDSA P-256, clé publique éditeur gravée dans le bootloader),
   double banque A/B avec retour arrière automatique si la nouvelle image ne confirme pas
   son démarrage. Sur STM32 : bootloader type MCUboot ou SBSFU (STM32 Secure Boot).
2. **Stockage flash à double page (journalisé)** — aujourd'hui une coupure pendant
   l'effacement de la page LINK régénère l'identité. Écrire alternativement sur deux
   pages avec compteur de génération.
3. **Permissions par rôle** — mots de passe distincts `admin` / `user` / `lecture`,
   et drapeaux par commande (`LINK_CMD_ROLE_ADMIN`…). `CHPWD` et `FRESET` réservés à l'admin.
4. **Renouvellement de clés** (`REKEY`) pour les sessions très longues (passerelles
   24/7), plutôt qu'un nouveau `HELLO`.

## Proposé — priorité moyenne

5. **PAKE (SPAKE2+ / CPace)** à la place de « signature + preuve PBKDF2 » : supprime
   le risque d'attaque par dictionnaire hors-ligne lors d'un premier contact MITM
   en mode TOFU. Plus complexe à implémenter sur MCU, à garder pour une v2.1.
6. **Élément sécurisé** (ATECC608B, SE050, OPTIGA Trust M) : clé d'identité non
   extractible, RNG certifié, compteur monotone matériel. L'interface
   `link_crypto.h` est prévue pour ça.
7. **Découverte mDNS côté client** (`_link._tcp`) en complément de l'UDP : plus
   fiable sur les réseaux d'entreprise qui filtrent la diffusion.
8. **Passerelle BLE ↔ Wi-Fi** (ESP32) : un ESP32 relaie les devices BLE vers le LAN
   sans jamais déchiffrer (la session est de bout en bout).
9. **Types d'arguments** : un schéma léger (entier, flottant, binaire, booléen)
   déclaré par commande, avec `GETCMDS` qui liste les commandes d'un device
   (auto-documentation, génération d'IHM dans les applis).
10. **Transport Windows BLE** : implémentation `ILinkGattConnection` basée sur
    `Windows.Devices.Bluetooth` pour les exemples WPF/WinUI.

## Proposé — outillage

11. **LINK Studio** : appli multiplateforme (MAUI/Avalonia) de diagnostic — scan
    USB/BLE/Wi-Fi, console de commandes, journal des trames déchiffrées, provisioning.
12. **Fuzzing** du décodeur device (libFuzzer sur `link_channel_input`) et du
    `LinkStreamDecoder` (SharpFuzz) dans la CI.
13. **Mettre à jour le simulateur Python** v1 (ou le remplacer par
    `link_host_device`, qui exécute le vrai code C).
14. **Kotlin / Swift** : si une appli Android native (sans .NET) ou iOS est
    envisagée, le protocole est assez simple pour un portage (≈ 600 lignes),
    `tools/linkctl.py` sert de référence lisible.
15. **Dépréciation v1** : marquer `AuthenticateAsync` / `ChangePasswordAsync` v1
    `[Obsolete]` dans une version 3.0 et retirer SHA-1 de `LinkHashProviderFactory`.
