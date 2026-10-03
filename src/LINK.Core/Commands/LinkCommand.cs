namespace Link.Core.Commands;

/// <summary>Commandes standard du protocole LINK.</summary>
public static class LinkCommand
{
    public const string GetApp = "GETAPP";
    public const string GetVersion = "GETV";
    public const string Return = "RETURN";
    public const string Auth = "AUTH";
    public const string AuthInit = "AUTH_INIT";
    public const string ChangePassword = "CHPWD";
    public const string Done = "DONE";

    // ---- LINK v2 ----
    public const string Ping = "PING";
    public const string Discover = "DISCOVER";
    /// <summary>Poignée de main de la session sécurisée.</summary>
    public const string Hello = "HELLO";
    /// <summary>Provisioning initial du mot de passe (device non provisionné).</summary>
    public const string SetPassword = "SETPWD";
    /// <summary>Réinitialisation usine (session authentifiée).</summary>
    public const string FactoryReset = "FRESET";
}
