namespace Link.Client.Models;

public sealed record LinkDeviceInfo
{
    public string AppId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? Uid { get; init; }
    public string? Model { get; init; }
    public bool IsLocked { get; init; }
    public string EncryptionMode { get; init; } = "NONE";
    public string HashMethod { get; init; } = "NONE";

    // ---- LINK v2 ----

    /// <summary>Versions de protocole supportées (<c>PROTO=1,2</c>). Vide : device v1.</summary>
    public IReadOnlyList<int> Protocols { get; init; } = Array.Empty<int>();

    /// <summary>Version du firmware (<c>FW=</c>).</summary>
    public string? Firmware { get; init; }

    /// <summary>Empreinte de la clé d'identité (<c>FP=</c>), à comparer à l'étiquette du device.</summary>
    public string? Fingerprint { get; init; }

    /// <summary>Un mot de passe est défini (<c>PROV=1</c>).</summary>
    public bool IsProvisioned { get; init; }

    /// <summary>Méthode d'authentification v2 (<c>AUTH=PBKDF2-SHA256</c>).</summary>
    public string? AuthMethod { get; init; }

    /// <summary>Transports exposés par le device (<c>TRANSPORTS=USB,BLE,WIFI</c>).</summary>
    public IReadOnlyList<string> Transports { get; init; } = Array.Empty<string>();

    /// <summary>Taille maximale de trame acceptée (<c>MAXFRAME=</c>).</summary>
    public int? MaxFrameSize { get; init; }

    /// <summary>Le device supporte la session sécurisée LINK v2.</summary>
    public bool SupportsV2 => Protocols.Contains(2);

    public static LinkDeviceInfo Parse(string appId, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            throw new ArgumentException("GETV response has no arguments");

        var info = new LinkDeviceInfo
        {
            AppId = appId,
            Version = args[0]
        };

        foreach (var arg in args.Skip(1))
        {
            var parts = arg.Split('=', 2);
            if (parts.Length != 2)
                continue;

            info = parts[0] switch
            {
                "UID" => info with { Uid = parts[1] },
                "MODEL" => info with { Model = parts[1] },
                "ENC" => info with { EncryptionMode = parts[1] },
                "HASH" => info with { HashMethod = parts[1] },
                "LOCKED" => info with { IsLocked = parts[1] == "true" },
                "PROTO" => info with { Protocols = ParseInts(parts[1]) },
                "FW" => info with { Firmware = parts[1] },
                "FP" => info with { Fingerprint = parts[1] },
                "PROV" => info with { IsProvisioned = parts[1] == "1" },
                "AUTH" => info with { AuthMethod = parts[1] },
                "TRANSPORTS" => info with { Transports = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries) },
                "MAXFRAME" => info with { MaxFrameSize = int.TryParse(parts[1], out var m) ? m : null },
                _ => info
            };
        }

        return info;
    }

    private static int[] ParseInts(string csv)
        => csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
              .Select(s => int.TryParse(s, out var v) ? v : -1)
              .Where(v => v > 0)
              .ToArray();
}
