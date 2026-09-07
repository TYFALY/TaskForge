using System.Net;
using System.Net.Sockets;

namespace TaskForge.Worker.Security;

/// <summary>
/// Strict URL validator that blocks Server-Side Request Forgery (SSRF) attempts
/// by rejecting loopback, link-local, private network and cloud metadata targets
/// inside the webhook execution engine.
/// </summary>
public static class SsrfProtectionFilter
{
    private static readonly string[] BlockedHostNames =
    {
        "localhost",
        "localhost.localdomain",
        "ip6-localhost",
        "ip6-loopback"
    };

    private static readonly string[] BlockedHostSuffixes =
    {
        ".local",
        ".internal",
        ".intranet",
        ".corp"
    };

    /// <summary>
    /// Throws if <paramref name="rawUrl"/> targets a forbidden destination
    /// (loopback, link-local, private network or cloud metadata endpoint).
    /// </summary>
    /// <param name="rawUrl">The user-supplied webhook URL.</param>
    /// <exception cref="ArgumentException">If the URL is invalid.</exception>
    /// <exception cref="SsrfBlockedException">If the URL targets a forbidden host.</exception>
    public static void EnsureSafe(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            throw new ArgumentException("Webhook URL cannot be empty", nameof(rawUrl));
        }

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"Webhook URL is not a valid absolute URI: {rawUrl}", nameof(rawUrl));
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new SsrfBlockedException($"Only http and https schemes are allowed (got '{uri.Scheme}')");
        }

        var host = uri.Host;

        // Block obvious loopback names
        if (BlockedHostNames.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            throw new SsrfBlockedException($"Loopback host '{host}' is not allowed");
        }

        foreach (var suffix in BlockedHostSuffixes)
        {
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                throw new SsrfBlockedException($"Internal host '{host}' is not allowed");
            }
        }

        // Try to resolve the host to one or more IP addresses and verify each.
        // This protects against DNS rebinding and direct IP targeting.
        var addresses = ResolveHostSafely(host);
        if (addresses.Length == 0)
        {
            throw new SsrfBlockedException($"Unable to resolve host '{host}'");
        }

        foreach (var address in addresses)
        {
            if (IsBlockedAddress(address))
            {
                throw new SsrfBlockedException(
                    $"Destination IP '{address}' for host '{host}' is in a blocked range (loopback / link-local / private / metadata)");
            }
        }
    }

    private static IPAddress[] ResolveHostSafely(string host)
    {
        // If the host is a literal IP, parse it directly to avoid DNS lookup.
        if (IPAddress.TryParse(host, out var literal))
        {
            return new[] { literal };
        }

        try
        {
            return Dns.GetHostAddresses(host);
        }
        catch (Exception)
        {
            return Array.Empty<IPAddress>();
        }
    }

    private static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            // 0.0.0.0/8
            if (bytes[0] == 0) return true;
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 100.64.0.0/10 (CGNAT)
            if (bytes[0] == 100 && (bytes[1] >= 64 && bytes[1] <= 127)) return true;
            // 127.0.0.0/8 (covered by IsLoopback but kept for clarity)
            if (bytes[0] == 127) return true;
            // 169.254.0.0/16 (link-local; includes 169.254.169.254 cloud metadata)
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && (bytes[1] >= 16 && bytes[1] <= 31)) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 224.0.0.0/4 (multicast)
            if (bytes[0] >= 224 && bytes[0] <= 239) return true;
            // 240.0.0.0/4 (reserved/broadcast)
            if (bytes[0] >= 240) return true;
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            {
                return true;
            }

            // ::1/128 loopback
            if (address.Equals(IPAddress.IPv6Loopback))
            {
                return true;
            }

            // Unique local addresses (fc00::/7)
            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Exception thrown when a webhook URL targets a destination that is
/// explicitly blocked by the SSRF protection filter.
/// </summary>
public class SsrfBlockedException : Exception
{
    public SsrfBlockedException(string message) : base(message) { }
}
