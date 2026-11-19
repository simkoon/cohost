namespace CoHost.Contracts;

/// <summary>Database state reported by <c>/api/server-info</c>. Wire values are snake_case.</summary>
public enum DatabaseStatus
{
    Ok,
    MigrationsPending,
    Unavailable,
}

/// <summary>Response of <c>GET /api/server-info</c>.</summary>
public sealed record ServerInfo(
    string AppName,
    string ServerVersion,
    string ProtocolVersion,
    string CertSha256,
    DatabaseStatus Database,
    bool DefaultDbPassword);
