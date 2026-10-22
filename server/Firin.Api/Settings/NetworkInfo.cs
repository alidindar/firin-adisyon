using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Firin.Api.Settings;

public static class NetworkInfo
{
    private static readonly string[] VirtualMarkers = ["Virtual", "Hyper-V", "VPN", "VMware", "Docker", "WSL"];

    /// <summary>Tabletlerin bağlanabileceği yerel ağ adresleri, örn. http://192.168.1.20:5080</summary>
    public static List<string> LanUrls(int port)
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToList();
        // Sanal kartlar (Hyper-V, VPN, Docker...) tabletten ulaşılamayan adresler verir; ağ geçidi olan gerçek kartlar öncelikli.
        var real = candidates.Where(IsRealLan).ToList();
        return Urls(real.Count > 0 ? real : candidates, port);
    }

    private static bool IsRealLan(NetworkInterface n) =>
        n.NetworkInterfaceType is not (NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp)
        && !VirtualMarkers.Any(m => n.Description.Contains(m, StringComparison.OrdinalIgnoreCase))
        && n.GetIPProperties().GatewayAddresses.Any(g =>
            g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

    private static List<string> Urls(IEnumerable<NetworkInterface> nics, int port) => nics
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Select(a => a.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)
            && !a.ToString().StartsWith("169.254.", StringComparison.Ordinal))
        .Select(a => $"http://{a}:{port}")
        .Distinct()
        .ToList();

    /// <summary>"Urls" ayarındaki ilk adresin portu; okunamazsa 5080.</summary>
    public static int Port(IConfiguration config)
    {
        var first = config["Urls"]?.Split(';')[0].Replace("0.0.0.0", "localhost").Replace("*", "localhost");
        return Uri.TryCreate(first, UriKind.Absolute, out var uri) ? uri.Port : 5080;
    }
}
