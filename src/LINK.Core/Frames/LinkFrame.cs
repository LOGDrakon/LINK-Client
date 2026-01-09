namespace Link.Core.Frames;

public sealed class LinkFrame
{
    public string? AppId { get; }
    public string Command { get; }
    public IReadOnlyList<string> Arguments { get; }

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

    public override string ToString()
    {
        var parts = new List<string> { "LINK" };

        if (!string.IsNullOrEmpty(AppId))
            parts.Add(AppId);

        parts.Add(Command);
        parts.AddRange(Arguments);

        return string.Join(':', parts) + '\0';
    }
}
