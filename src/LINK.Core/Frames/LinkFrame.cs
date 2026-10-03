namespace Link.Core.Frames;

public sealed class LinkFrame
{
    public string? AppId { get; }
    public string Command { get; }
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>Version du protocole de la trame reçue/émise (1 = texte, 2 = binaire).</summary>
    public int Version { get; init; } = 1;

    /// <summary>Numéro de séquence v2 (0 en v1 et pour les évènements).</summary>
    public ushort Sequence { get; init; }

    /// <summary>La trame a circulé chiffrée (session LINK v2).</summary>
    public bool IsEncrypted { get; init; }

    /// <summary>Trame non sollicitée émise par le device (drapeau EVENT v2).</summary>
    public bool IsEvent { get; init; }

    public bool IsReturn => Command == "RETURN";

    public string? ReturnedCommand =>
        IsReturn && Arguments.Count > 0 ? Arguments[0] : null;

    public IReadOnlyList<string> ReturnArguments =>
        IsReturn && Arguments.Count > 1
            ? Arguments.Skip(1).ToArray()
            : Array.Empty<string>();

    public LinkFrame(string? appId, string command, params string[] arguments)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command cannot be empty");

        AppId = appId;
        Command = command;
        Arguments = arguments ?? Array.Empty<string>();
    }

    /// <summary>Représentation texte LINK v1 (<c>LINK\x1f…\0</c>).</summary>
    public override string ToString()
    {
        var parts = new List<string> { "LINK" };

        if (!string.IsNullOrEmpty(AppId))
            parts.Add(AppId);

        parts.Add(Command);
        parts.AddRange(Arguments);

        return string.Join('\x1f', parts) + '\0';
    }
}
