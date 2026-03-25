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
                _ => info
            };
        }

        return info;
    }
}
