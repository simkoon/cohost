using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CoHost.Server;

/// <summary>TLS certificate plus its SHA-256 fingerprint (uppercase hex, no colons).</summary>
public sealed class TlsMaterial(X509Certificate2 certificate, string sha256Hex, bool generated) : IDisposable
{
    public X509Certificate2 Certificate { get; } = certificate;

    public string Sha256Hex { get; } = sha256Hex;

    public bool Generated { get; } = generated;

    public void Dispose() => Certificate.Dispose();
}

/// <summary>Loads the operator-provided or previously generated certificate, or creates a self-signed one (D21).</summary>
public static class CertBootstrap
{
    public const string CertFileName = "cert.pem";
    public const string KeyFileName = "key.pem";

    public static TlsMaterial LoadOrCreate(string certDir, IReadOnlyCollection<string> hostnames)
    {
        Directory.CreateDirectory(certDir);
        var certPath = Path.Combine(certDir, CertFileName);
        var keyPath = Path.Combine(certDir, KeyFileName);
        var hasCert = File.Exists(certPath);
        var hasKey = File.Exists(keyPath);

        if (hasCert != hasKey)
        {
            // Never regenerate silently: a changed fingerprint disconnects every pinned agent (Pitfall 8).
            var missing = hasCert ? KeyFileName : CertFileName;
            throw new InvalidOperationException(
                $"TLS material in '{certDir}' is incomplete: {missing} is missing. Restore it or remove both files to generate a new certificate.");
        }

        var generated = false;
        if (!hasCert)
        {
            Generate(certPath, keyPath, hostnames);
            generated = true;
        }

        var cert = X509Certificate2.CreateFromPemFile(certPath, keyPath);
        return new TlsMaterial(cert, Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256)), generated);
    }

    private static void Generate(string certPath, string keyPath, IReadOnlyCollection<string> hostnames)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=CoHost Server", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        foreach (var host in hostnames)
        {
            if (IPAddress.TryParse(host, out var ip))
            {
                san.AddIpAddress(ip);
            }
            else
            {
                san.AddDnsName(host);
            }
        }

        request.CertificateExtensions.Add(san.Build());

        var now = DateTimeOffset.UtcNow;
        using var cert = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(10));

        File.WriteAllText(certPath, cert.ExportCertificatePem());
        KeyFiles.WritePrivate(keyPath, key.ExportPkcs8PrivateKeyPem());
    }
}

internal static class KeyFiles
{
    /// <summary>Writes a private key file readable only by the owner (0600 on Unix).</summary>
    public static void WritePrivate(string path, string content)
    {
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(path, content);
            return;
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        };
        using (var stream = new FileStream(path, options))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(content);
        }

        // UnixCreateMode is subject to umask and ignored for existing files; enforce explicitly.
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
