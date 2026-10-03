using System.Text.Json;

namespace Link.Client.Security;

/// <summary>Épinglage persistant dans un fichier JSON (ex. dossier de données de l'application).</summary>
public sealed class LinkFileTrustStore : ILinkTrustStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private Dictionary<string, string> _pins;

    public LinkFileTrustStore(string path)
    {
        _path = path;
        _pins = Load(path);
    }

    /// <summary>Emplacement par défaut : %APPDATA%/LINK/known_devices.json (ou ~/.config/LINK).</summary>
    public static LinkFileTrustStore CreateDefault()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LINK");
        return new LinkFileTrustStore(Path.Combine(dir, "known_devices.json"));
    }

    public string? GetFingerprint(string deviceKey)
    {
        lock (_lock)
            return _pins.TryGetValue(deviceKey, out var fp) ? fp : null;
    }

    public void SetFingerprint(string deviceKey, string fingerprint)
    {
        lock (_lock)
        {
            _pins[deviceKey] = fingerprint;
            Save();
        }
    }

    public void Remove(string deviceKey)
    {
        lock (_lock)
        {
            if (_pins.Remove(deviceKey))
                Save();
        }
    }

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                       ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            // fichier corrompu : on repart d'une base vide (les devices seront ré-épinglés)
        }
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_pins, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }
}
