namespace Link.Transport.Tcp;

public sealed class LinkTcpOptions
{
    /// <summary>Hostname or IP address to connect to.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>TCP port to connect to.</summary>
    public int Port { get; set; } = 5000;

    /// <summary>Timeout for the initial TCP connection attempt.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Timeout waiting for a response after sending a command.
    /// Unused by the transport itself but exposed for convenience.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
