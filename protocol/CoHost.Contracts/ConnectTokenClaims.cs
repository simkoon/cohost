namespace CoHost.Contracts;

/// <summary>Claim names and limits of the short-lived remote connect token (SPEC 4.1, 5.2).</summary>
public static class ConnectTokenClaims
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Device = "dev";
    public const string SessionId = "sid";
    public const string JwtId = "jti";
    public const string Expires = "exp";

    public const string Algorithm = "ES256";
    public const int LifetimeSeconds = 60;
    public const int JtiReplayWindowSeconds = 300;
}
