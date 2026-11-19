namespace CoHost.Contracts;

/// <summary>
/// Wire protocol version shared by server, agent and client.
/// MAJOR changes are breaking; MINOR changes only add optional fields or message types.
/// </summary>
public static class ProtocolInfo
{
    public const int Major = 0;
    public const int Minor = 1;
    public const string Version = "0.1";

    /// <summary>Default heartbeat interval in seconds an agent uses until the server says otherwise.</summary>
    public const int DefaultHeartbeatIntervalSec = 15;
}
