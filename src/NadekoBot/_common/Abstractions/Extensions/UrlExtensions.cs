using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Nadeko.Common;

public static class UrlExtensions
{
    // A quick pre-check for user input. Only ExternalHttp checks the address which a request really connects to.
    public static bool IsPublicUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not ("http" or "https"))
            return false;

        var host = uri.IdnHost;
        if (host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host is "localhost" or "metadata.google.internal")
            return false;

        if (IPAddress.TryParse(host, out var ip))
            return IsPublicAddress(ip);

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            if (addresses.Length == 0)
                return false;

            for (var i = 0; i < addresses.Length; i++)
            {
                if (!IsPublicAddress(addresses[i]))
                    return false;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    public static bool IsPublicAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> b4 = stackalloc byte[4];
            return ip.TryWriteBytes(b4, out _) && IsPublicV4(BinaryPrimitives.ReadUInt32BigEndian(b4));
        }

        if (ip.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        Span<byte> b = stackalloc byte[16];
        if (!ip.TryWriteBytes(b, out _))
            return false;

        // Only global unicast (2000::/3) reaches the internet.
        if ((b[0] & 0xE0) != 0x20)
            return false;

        var first32 = BinaryPrimitives.ReadUInt32BigEndian(b);

        // Teredo 2001::/32 and documentation 2001:db8::/32
        if (first32 is 0x20010000 or 0x20010DB8)
            return false;

        // 6to4 2002::/16 carries an IPv4 address in the next 32 bits.
        if (first32 >> 16 == 0x2002)
            return IsPublicV4(BinaryPrimitives.ReadUInt32BigEndian(b[2..]));

        return true;
    }

    private static bool IsPublicV4(uint a)
    {
        return !(InRange(a, 0x00000000, 8)       // 0.0.0.0/8
                 || InRange(a, 0x0A000000, 8)    // 10.0.0.0/8
                 || InRange(a, 0x64400000, 10)   // 100.64.0.0/10 carrier-grade NAT
                 || InRange(a, 0x7F000000, 8)    // 127.0.0.0/8
                 || InRange(a, 0xA9FE0000, 16)   // 169.254.0.0/16 link-local, cloud metadata
                 || InRange(a, 0xAC100000, 12)   // 172.16.0.0/12
                 || InRange(a, 0xC0000000, 24)   // 192.0.0.0/24
                 || InRange(a, 0xC0000200, 24)   // 192.0.2.0/24
                 || InRange(a, 0xC0A80000, 16)   // 192.168.0.0/16
                 || InRange(a, 0xC6120000, 15)   // 198.18.0.0/15
                 || InRange(a, 0xC6336400, 24)   // 198.51.100.0/24
                 || InRange(a, 0xCB007100, 24)   // 203.0.113.0/24
                 || InRange(a, 0xE0000000, 3));  // 224.0.0.0/3 multicast, reserved, broadcast
    }

    private static bool InRange(uint address, uint network, int prefix)
        => (address >> (32 - prefix)) == (network >> (32 - prefix));
}
