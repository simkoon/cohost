using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace CoHost.Server;

public sealed record EcPublicJwk(
    [property: JsonPropertyName("kty")] string Kty,
    [property: JsonPropertyName("crv")] string Crv,
    [property: JsonPropertyName("x")] string X,
    [property: JsonPropertyName("y")] string Y,
    [property: JsonPropertyName("kid")] string Kid,
    [property: JsonPropertyName("use")] string Use,
    [property: JsonPropertyName("alg")] string Alg);

public sealed record JwksDocument(
    [property: JsonPropertyName("keys")] IReadOnlyList<EcPublicJwk> Keys);

/// <summary>ES256 token-signing key persisted as PKCS#8 PEM, exposed as a one-key JWKS (D11).</summary>
public sealed class SigningKeys : IDisposable
{
    public const string PrivateKeyFileName = "es256-private.pem";

    private SigningKeys(ECDsa key, string kid, JwksDocument jwks, bool generated)
    {
        Key = key;
        Kid = kid;
        Jwks = jwks;
        Generated = generated;
    }

    public ECDsa Key { get; }

    public string Kid { get; }

    public JwksDocument Jwks { get; }

    public bool Generated { get; }

    public static SigningKeys LoadOrCreate(string keyDir)
    {
        Directory.CreateDirectory(keyDir);
        var path = Path.Combine(keyDir, PrivateKeyFileName);

        var key = ECDsa.Create();
        bool generated;
        if (File.Exists(path))
        {
            key.ImportFromPem(File.ReadAllText(path));
            generated = false;
        }
        else
        {
            key.Dispose();
            key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            KeyFiles.WritePrivate(path, key.ExportPkcs8PrivateKeyPem());
            generated = true;
        }

        var parameters = key.ExportParameters(false);
        var x = Base64Url.EncodeToString(parameters.Q.X!);
        var y = Base64Url.EncodeToString(parameters.Q.Y!);
        var kid = ComputeThumbprint(x, y);
        var jwks = new JwksDocument([new EcPublicJwk("EC", "P-256", x, y, kid, "sig", "ES256")]);
        return new SigningKeys(key, kid, jwks, generated);
    }

    /// <summary>RFC 7638 JWK thumbprint of an EC P-256 public key.</summary>
    public static string ComputeThumbprint(string x, string y)
    {
        var json = "{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"" + x + "\",\"y\":\"" + y + "\"}";
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    public void Dispose() => Key.Dispose();
}
