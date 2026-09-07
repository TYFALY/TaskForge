using FluentAssertions;
using TaskForge.Core.Security;
using Xunit;

namespace TaskForge.Tests.Security;

public class SsrfProtectionFilterTests
{
    // Blocked IP ranges
    public static readonly TheoryData<string> BlockedLoopbackAddresses = new()
    {
        "127.0.0.1", "127.0.0.2", "127.255.255.255", "::1", "0.0.0.0"
    };

    public static readonly TheoryData<string> BlockedCloudMetadataUrls = new()
    {
        "http://169.254.169.254/latest/meta-data/",
        "http://metadata.google.internal/"
    };

    public static readonly TheoryData<string> BlockedPrivateNetworkUrls = new()
    {
        "http://10.0.0.1/api",
        "http://192.168.1.1/actuator",
        "http://172.16.0.1:8080/admin",
        "http://10.255.255.255/secret",
        "http://172.31.255.255/data"
    };

    public static readonly TheoryData<string> BlockedHostnames = new()
    {
        "localhost", "localhost.localdomain", "ip6-localhost",
        "something.internal", "server.corp", "host.local"
    };

    public static readonly TheoryData<string> AllowedExternalUrls = new()
    {
        "https://api.github.com/repos",
        "https://webhook.site/signal",
        "https://httpbin.org/post",
        "https://reqres.in/api/users"
    };

    [Theory]
    [MemberData(nameof(BlockedLoopbackAddresses))]
    public void EnsureSafe_WithLoopbackIp_ThrowsSsrfBlockedException(string ip)
    {
        var action = () => SsrfProtectionFilter.EnsureSafe($"http://{ip}/api");
        action.Should().Throw< SsrfBlockedException>();
    }

    [Theory]
    [MemberData(nameof(BlockedCloudMetadataUrls))]
    public void EnsureSafe_WithCloudMetadataEndpoint_ThrowsSsrfBlockedException(string url)
    {
        var action = () => SsrfProtectionFilter.EnsureSafe(url);
        action.Should().Throw< SsrfBlockedException>();
    }

    [Theory]
    [MemberData(nameof(BlockedPrivateNetworkUrls))]
    public void EnsureSafe_WithPrivateNetwork_ThrowsSsrfBlockedException(string url)
    {
        var action = () => SsrfProtectionFilter.EnsureSafe(url);
        action.Should().Throw< SsrfBlockedException>();
    }

    [Theory]
    [MemberData(nameof(BlockedHostnames))]
    public void EnsureSafe_WithBlockedHostname_ThrowsSsrfBlockedException(string hostname)
    {
        var action = () => SsrfProtectionFilter.EnsureSafe($"http://{hostname}/api");
        action.Should().Throw< SsrfBlockedException>();
    }

    [Theory]
    [MemberData(nameof(AllowedExternalUrls))]
    public void EnsureSafe_WithValidExternalUrl_DoesNotThrow(string url)
    {
        var action = () => SsrfProtectionFilter.EnsureSafe(url);
        action.Should().NotThrow();
    }

    [Fact]
    public void EnsureSafe_WithEmptyUrl_ThrowsArgumentException()
    {
        var action = () => SsrfProtectionFilter.EnsureSafe("");
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EnsureSafe_WithInvalidUri_ThrowsArgumentException()
    {
        var action = () => SsrfProtectionFilter.EnsureSafe("not-a-valid-url");
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EnsureSafe_WithNonHttpScheme_ThrowsSsrfBlockedException()
    {
        var action = () => SsrfProtectionFilter.EnsureSafe("file:///etc/passwd");
        action.Should().Throw< SsrfBlockedException>();
    }

    [Fact]
    public void EnsureSafe_WithNullUrl_ThrowsArgumentException()
    {
        var action = () => SsrfProtectionFilter.EnsureSafe(null);
        action.Should().Throw<ArgumentException>();
    }
}
