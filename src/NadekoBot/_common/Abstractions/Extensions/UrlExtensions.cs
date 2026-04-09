using System.Net;
using System.Net.Sockets;

namespace Nadeko.Common;

public static class UrlExtensions
{
    public static bool IsPublicUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not ("http" or "https"))
            return false;

        if (uri.IsLoopback)
            return false;

        var host = uri.Host;

        if (host is "localhost" or "metadata.google.internal")
            return false;

        if (IPAddress.TryParse(host, out var ip))
            return !IsPrivateOrReservedIp(ip);

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            for (var i = 0; i < addresses.Length; i++)
            {
                if (IsPrivateOrReservedIp(addresses[i]))
                    return false;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    private static bool IsPrivateOrReservedIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                return true;

            if (ip.Equals(IPAddress.IPv6Loopback))
                return true;
        }

        Span<byte> bytes = stackalloc byte[ip.AddressFamily == AddressFamily.InterNetworkV6 ? 16 : 4];
        if (!ip.TryWriteBytes(bytes, out _))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork && bytes.Length >= 4)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10)
                return true;

            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;

            // 169.254.0.0/16 (link-local / cloud metadata)
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;

            // 127.0.0.0/8
            if (bytes[0] == 127)
                return true;

            // 0.0.0.0/8
            if (bytes[0] == 0)
                return true;
        }

        return false;
    }
}
