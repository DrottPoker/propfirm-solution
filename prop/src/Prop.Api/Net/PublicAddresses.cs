using System.Net;
using System.Net.Sockets;

namespace Prop.Api.Net;

/// <summary>
/// Calls to addresses a firm chose, such as its webhook, reach only the public internet (ADR 0044). Otherwise a firm could
/// make our servers call our own network, or the cloud's, and see from the answers what is there.
/// </summary>
internal static class PublicAddresses
{
    /// <summary>Why a call to the host was not made.</summary>
    public static string NotPublicProblem(string host) => $"{host} is not on the public internet, so we do not call it.";

    /// <summary>
    /// A handler that connects only to public addresses, looked up when it connects, so a name that points somewhere else
    /// later is caught too. It does not follow redirects, which could lead anywhere.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        ConnectCallback = ConnectAsync,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    /// <summary>Whether the host is a name, or a public address. Names are looked up when called, by <see cref="CreateHandler"/>.</summary>
    public static bool MayBeCalled(string host)
    {
        var name = host.Trim('[', ']').TrimEnd('.').ToLowerInvariant();
        if (IPAddress.TryParse(name, out var address))
        {
            return IsPublic(address);
        }

        return name.Contains('.', StringComparison.Ordinal)
            && !name.EndsWith(".localhost", StringComparison.Ordinal)
            && !name.EndsWith(".local", StringComparison.Ordinal)
            && !name.EndsWith(".internal", StringComparison.Ordinal);
    }

    /// <summary>Whether the address is on the public internet: not loopback, private, link-local, shared, reserved or multicast.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(b[0] is 0 or 10 or 127 or >= 224
                || (b[0] == 100 && b[1] is >= 64 and <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)
                || (b[0] == 192 && b[1] == 88 && b[2] == 99)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 198 && b[1] is 18 or 19)
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)
                || (b[0] == 203 && b[1] == 0 && b[2] == 113));
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        // NAT64 (64:ff9b::/96) and 6to4 (2002::/16) carry an IPv4 address, which decides.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0)
        {
            return IsPublic(new IPAddress(b.AsSpan(12, 4)));
        }

        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return IsPublic(new IPAddress(b.AsSpan(2, 4)));
        }

        // Documentation (2001:db8::/32) is not real. Otherwise only global unicast (2000::/3) is public.
        return !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8) && (b[0] & 0xe0) == 0x20;
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, cancellationToken);
        var allowed = addresses.Where(IsPublic).ToArray();
        if (allowed.Length == 0)
        {
            throw new HttpRequestException(NotPublicProblem(host));
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
