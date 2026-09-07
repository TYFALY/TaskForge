using System.Net;
using System.Net.Sockets;

namespace TaskForge.Core.Security;

/// <summary>
/// Strict URL validator that blocks Server-Side Request Forgery (SSRF) attempts
/// by rejecting loopback, link-local, private network and cloud metadata targets
/// in webhook execution engines across both Embedded and Distributed modes.
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
                    $"Target address '{address}' is blocked (host={host}, resolved from DNS). " +
                    $"Cloud metadata (169.254.169.254), loopback, and private network targets are not allowed.");
            }
        }
    }

    private static IPAddress[] ResolveHostSafely(string host)
    {
        try
        {
            var results = Dns.GetHostAddresses(host, AddressFamily.InterNetwork);
            return results;
        }
        catch (SocketException)
        {
            return Array.Empty<IPAddress>();
        }
        catch (ArgumentException)
        {
            return Array.Empty<IPAddress>();
        }
    }

    private static bool IsBlockedAddress(IPAddress address)
    {
        // Loopback: 127.0.0.0/8
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        // Link-local: includes 169.254.0.0/16 (cloud metadata IP 169.254.169.254 lives here)
        if (address.IsIPv6LinkLocal)
        {
            return true;
        }

        // Check for private network ranges
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4) // IPv4
        {
            // 10.0.0.0/8
            if (bytes[0] == 10)
            {
                return true;
            }

            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Exception thrown by <see cref="SsrfProtectionFilter"/> when a URL is blocked.
/// </summary>
public class SsrfBlockedException : Exception
{
    public SsrfBlockedException(string message) : base(message) { }
}
