namespace CoHost.Contracts;

/// <summary>Application-defined WebSocket close codes (D24).</summary>
public static class WsCloseCodes
{
    public const int AuthFailed = 4401;
    public const int DuplicateConnection = 4409;
    public const int UpgradeRequired = 4426;
}
