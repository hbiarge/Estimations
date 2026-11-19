using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace Acheve.Common.Shared
{
    /// <summary>
    /// Keeps the HTTP clients that call a URL given by a customer (the images to download, the callback to notify)
    /// from reaching our own network: loopback, private, link-local (cloud metadata), carrier-grade NAT and
    /// other addresses that aren't on the public internet.
    /// The check runs when the connection is opened, on the address the name resolved to, so it also covers
    /// redirects, IP literals and names that point to an internal address.
    /// </summary>
    public static class OutboundAddressGuard
    {
        /// <summary>
        /// Lets the clients reach private addresses. Off by default; the local development setup turns it on
        /// because the fake external systems and the customers' callbacks run on localhost.
        /// </summary>
        public const string AllowPrivateAddressesKey = "Outbound:AllowPrivateAddresses";

        /// <summary>The primary handler for a client that calls customer-supplied URLs.</summary>
        public static SocketsHttpHandler CreateHandler(IConfiguration configuration) =>
            CreateHandler(bool.TryParse(configuration[AllowPrivateAddressesKey], out var allow) && allow);

        public static SocketsHttpHandler CreateHandler(bool allowPrivateAddresses) =>
            new()
            {
                // A proxy would connect for us, and the check would see the proxy's address instead of the target's.
                UseProxy = false,
                ConnectCallback = (context, cancellationToken) =>
                    ConnectAsync(context.DnsEndPoint, allowPrivateAddresses, cancellationToken)
            };

        /// <summary>True when the address is on the public internet.</summary>
        public static bool IsPublic(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (IPAddress.IsLoopback(address))
            {
                return false;
            }

            var bytes = address.GetAddressBytes();

            return address.AddressFamily switch
            {
                AddressFamily.InterNetwork => IsPublicV4(bytes),
                AddressFamily.InterNetworkV6 => IsPublicV6(address, bytes),
                _ => false
            };
        }

        private static bool IsPublicV4(byte[] b) =>
            !(b[0] == 0                                    // "this" network, 0.0.0.0
              || b[0] == 10                                // private
              || (b[0] == 100 && b[1] is >= 64 and <= 127) // carrier-grade NAT
              || (b[0] == 169 && b[1] == 254)              // link-local, cloud metadata
              || (b[0] == 172 && b[1] is >= 16 and <= 31)  // private
              || (b[0] == 192 && b[1] == 0 && b[2] == 0)   // protocol assignments
              || (b[0] == 192 && b[1] == 168)              // private
              || (b[0] == 198 && b[1] is 18 or 19)         // benchmarking
              || b[0] >= 224);                             // multicast and reserved

        private static bool IsPublicV6(IPAddress address, byte[] b)
        {
            if (address.Equals(IPAddress.IPv6Any)
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || address.IsIPv6UniqueLocal)
            {
                return false;
            }

            // Documentation range, 2001:db8::/32.
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8)
            {
                return false;
            }

            // NAT64, 64:ff9b::/96, and 6to4, 2002::/16, carry an IPv4 address: it decides.
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b[4..12].All(x => x == 0))
            {
                return IsPublic(new IPAddress(b[12..16]));
            }

            if (b[0] == 0x20 && b[1] == 0x02)
            {
                return IsPublic(new IPAddress(b[2..6]));
            }

            return true;
        }

        private static async ValueTask<Stream> ConnectAsync(
            DnsEndPoint endPoint, bool allowPrivateAddresses, CancellationToken cancellationToken)
        {
            var addresses = await Dns.GetHostAddressesAsync(endPoint.Host, cancellationToken);

            var allowed = allowPrivateAddresses
                ? addresses
                : addresses.Where(IsPublic).ToArray();

            if (allowed.Length == 0)
            {
                // The message doesn't say which address the name resolved to: the customer can read it.
                throw addresses.Length == 0
                    ? new SocketException((int)SocketError.HostNotFound)
                    : new InvalidOperationException($"The host {endPoint.Host} is not on the public internet.");
            }

            Exception? last = null;

            foreach (var address in allowed)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, endPoint.Port), cancellationToken);

                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    socket.Dispose();
                    last = e;
                }
            }

            throw last!;
        }
    }
}
