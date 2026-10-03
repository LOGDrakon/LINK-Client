using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Link.Transport.Tcp;

/// <summary>Device LINK trouvé sur le réseau local.</summary>
public sealed record LinkLanDevice
{
    public required IPAddress Address { get; init; }
    public required string AppId { get; init; }
    public int Port { get; init; } = 5000;
    public string? Uid { get; init; }
    public string? Model { get; init; }
    public string? Name { get; init; }
    public string? Fingerprint { get; init; }
    public IReadOnlyList<int> Protocols { get; init; } = Array.Empty<int>();
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();

    /// <summary>Options TCP prêtes à l'emploi pour se connecter à ce device.</summary>
    public LinkTcpOptions ToTcpOptions() => new() { Host = Address.ToString(), Port = Port };
}

/// <summary>
/// Découverte des devices LINK Wi-Fi/Ethernet : diffusion UDP de <c>LINK\x1fDISCOVER\0</c>
/// (port 47800) et collecte des réponses (docs/LINK_Protocol_v2.md §7).
/// </summary>
public static class LinkLanDiscovery
{
    public const int DefaultPort = 47800;
    public static readonly IPAddress MulticastGroup = IPAddress.Parse("239.255.76.75");

    private static readonly byte[] Request = Encoding.ASCII.GetBytes("LINK\x1f" + "DISCOVER\0");

    public static async Task<IReadOnlyList<LinkLanDevice>> ScanAsync(
        TimeSpan? timeout = null,
        string? appIdFilter = null,
        int port = DefaultPort,
        IEnumerable<IPAddress>? extraTargets = null,
        CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<string, LinkLanDevice>();
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        foreach (var target in Targets(extraTargets))
        {
            try
            {
                await udp.SendAsync(Request, new IPEndPoint(target, port), cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // interface sans diffusion : ignorée
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(1.5));
        try
        {
            while (true)
            {
                var result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                var device = ParseResponse(result.Buffer, result.RemoteEndPoint.Address);
                if (device is null)
                    continue;
                if (appIdFilter is not null && !string.Equals(device.AppId, appIdFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                found[$"{device.Address}/{device.Uid}"] = device;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // fin de la fenêtre d'écoute
        }

        return found.Values.ToArray();
    }

    /// <summary>Analyse une réponse <c>LINK\x1f&lt;APP&gt;\x1fRETURN\x1fDISCOVER\x1fCLE=VALEUR…</c>.</summary>
    public static LinkLanDevice? ParseResponse(byte[] datagram, IPAddress from)
    {
        var text = Encoding.UTF8.GetString(datagram).TrimEnd('\0');
        var parts = text.Split('\x1f');
        if (parts.Length < 4 || parts[0] != "LINK" || parts[2] != "RETURN" || parts[3] != "DISCOVER")
            return null;

        var props = parts.Skip(4)
            .Select(p => p.Split('=', 2))
            .Where(kv => kv.Length == 2)
            .GroupBy(kv => kv[0])
            .ToDictionary(g => g.Key, g => g.Last()[1]);

        return new LinkLanDevice
        {
            Address = from,
            AppId = parts[1],
            Port = props.TryGetValue("PORT", out var p) && int.TryParse(p, out var port) ? port : 5000,
            Uid = props.GetValueOrDefault("UID"),
            Model = props.GetValueOrDefault("MODEL"),
            Name = props.GetValueOrDefault("NAME"),
            Fingerprint = props.GetValueOrDefault("FP"),
            Protocols = props.TryGetValue("PROTO", out var proto)
                ? proto.Split(',').Select(s => int.TryParse(s, out var v) ? v : 0).Where(v => v > 0).ToArray()
                : Array.Empty<int>(),
            Properties = props,
        };
    }

    private static IEnumerable<IPAddress> Targets(IEnumerable<IPAddress>? extra)
    {
        var targets = new HashSet<IPAddress> { IPAddress.Broadcast, MulticastGroup };

        // Diffusion dirigée sur chaque interface (certains routeurs filtrent 255.255.255.255).
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask is null)
                        continue;
                    var ip = ua.Address.GetAddressBytes();
                    var mask = ua.IPv4Mask.GetAddressBytes();
                    var bcast = new byte[4];
                    for (int i = 0; i < 4; i++)
                        bcast[i] = (byte)(ip[i] | ~mask[i]);
                    targets.Add(new IPAddress(bcast));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // plate-forme restreinte (Android sans permission) : diffusion générale seulement
        }

        if (extra is not null)
            foreach (var t in extra)
                targets.Add(t);
        return targets;
    }
}
