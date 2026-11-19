using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace CoHost.Server.Tests;

public sealed class CertBootstrapTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public void Generates_then_loads()
    {
        using var first = CertBootstrap.LoadOrCreate(_dir, []);
        Assert.True(first.Generated);
        Assert.True(File.Exists(Path.Combine(_dir, "cert.pem")));
        Assert.True(File.Exists(Path.Combine(_dir, "key.pem")));

        using var second = CertBootstrap.LoadOrCreate(_dir, []);
        Assert.False(second.Generated);
        Assert.Equal(first.Sha256Hex, second.Sha256Hex);
    }

    [Fact]
    public void Fingerprint_format()
    {
        using var tls = CertBootstrap.LoadOrCreate(_dir, []);
        Assert.Matches("^[0-9A-F]{64}$", tls.Sha256Hex);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(tls.Certificate.RawData)), tls.Sha256Hex);
    }

    [Fact]
    public void San_contains_hosts()
    {
        using var tls = CertBootstrap.LoadOrCreate(_dir, ["cohost.local", "192.0.2.10"]);
        var san = tls.Certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
        var dns = san.EnumerateDnsNames().ToList();
        var ips = san.EnumerateIPAddresses().ToList();
        Assert.Contains("localhost", dns);
        Assert.Contains("cohost.local", dns);
        Assert.Contains(IPAddress.Parse("127.0.0.1"), ips);
        Assert.Contains(IPAddress.Parse("192.0.2.10"), ips);
    }

    [Fact]
    public void Server_auth_ecdsa()
    {
        using var tls = CertBootstrap.LoadOrCreate(_dir, []);
        Assert.True(tls.Certificate.HasPrivateKey);
        using var ecdsa = tls.Certificate.GetECDsaPrivateKey();
        Assert.NotNull(ecdsa);
        var eku = tls.Certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();
        Assert.Contains(eku.EnhancedKeyUsages.Cast<Oid>(), o => o.Value == "1.3.6.1.5.5.7.3.1");
    }

    [Fact]
    public void Key_file_is_0600()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tls = CertBootstrap.LoadOrCreate(_dir, []);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(_dir, "key.pem")));
    }

    [Fact]
    public void Half_present_fails_fast()
    {
        Directory.CreateDirectory(_dir);
        var certPath = Path.Combine(_dir, "cert.pem");
        File.WriteAllText(certPath, "placeholder-cert-content");

        var ex = Assert.Throws<InvalidOperationException>(() => CertBootstrap.LoadOrCreate(_dir, []));
        Assert.Contains("key.pem", ex.Message);
        Assert.Equal("placeholder-cert-content", File.ReadAllText(certPath));
        Assert.False(File.Exists(Path.Combine(_dir, "key.pem")));
    }
}
