namespace Link.Client.Security;

/// <summary>Échec de sécurité : signature invalide, empreinte inattendue (MITM possible), paramètres refusés.</summary>
public sealed class LinkSecurityException : Exception
{
    public LinkSecurityException(string message) : base(message) { }
}

/// <summary>Le device a répondu <c>ERR &lt;code&gt;</c>.</summary>
public sealed class LinkDeviceErrorException : Exception
{
    public string Command { get; }
    public string Code { get; }
    public IReadOnlyList<string> Details { get; }

    public LinkDeviceErrorException(string command, string code, IReadOnlyList<string> details)
        : base($"{command} refusé par le device : {code}{(details.Count > 0 ? " " + string.Join(' ', details) : "")}")
    {
        Command = command;
        Code = code;
        Details = details;
    }
}
