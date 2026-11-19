using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace CoHost.Server.Tests;

public sealed class SigningKeysTests : IDisposable
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
        using var first = SigningKeys.LoadOrCreate(_dir);
        Assert.True(first.Generated);
        using var second = SigningKeys.LoadOrCreate(_dir);
        Assert.False(second.Generated);
        Assert.Equal(first.Kid, second.Kid);
        Assert.Equal(first.Jwks.Keys[0].X, second.Jwks.Keys[0].X);
        Assert.Equal(first.Jwks.Keys[0].Y, second.Jwks.Keys[0].Y);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(_dir, "es256-private.pem")));
        }
    }

    [Fact]
    public void Jwks_shape()
    {
        using var keys = SigningKeys.LoadOrCreate(_dir);
        var jwk = Assert.Single(keys.Jwks.Keys);
        Assert.Equal("EC", jwk.Kty);
        Assert.Equal("P-256", jwk.Crv);
        Assert.Equal("ES256", jwk.Alg);
        Assert.Equal("sig", jwk.Use);
        Assert.Equal(32, Base64Url.DecodeFromChars(jwk.X).Length);
        Assert.Equal(32, Base64Url.DecodeFromChars(jwk.Y).Length);
    }

    [Fact]
    public void Kid_is_rfc7638()
    {
        using var keys = SigningKeys.LoadOrCreate(_dir);
        var jwk = keys.Jwks.Keys[0];
        var json = "{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"" + jwk.X + "\",\"y\":\"" + jwk.Y + "\"}";
        var expected = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(expected, keys.Kid);
        Assert.Equal(expected, SigningKeys.ComputeThumbprint(jwk.X, jwk.Y));
        Assert.Equal(expected, jwk.Kid);
    }

    [Fact]
    public void Jwk_verifies_signature()
    {
        using var keys = SigningKeys.LoadOrCreate(_dir);
        var data = Encoding.UTF8.GetBytes("cohost-test");
        var sig = keys.Key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        var jwk = keys.Jwks.Keys[0];
        using var pub = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Base64Url.DecodeFromChars(jwk.X), Y = Base64Url.DecodeFromChars(jwk.Y) },
        });
        Assert.True(pub.VerifyData(data, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
}
