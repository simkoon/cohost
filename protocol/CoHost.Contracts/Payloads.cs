namespace CoHost.Contracts;

public sealed record AgentHelloPayload(
    Guid DeviceId,
    string AgentSecret,
    string ProtocolVersion,
    string AppVersion,
    InstallKind InstallKind);

public sealed record ClientHelloPayload(
    string AccessToken,
    string ProtocolVersion,
    string AppVersion,
    InstallKind InstallKind);

public sealed record HelloAckPayload(
    DateTimeOffset ServerTime,
    string ProtocolVersion,
    string MinProtocolVersion,
    int HeartbeatIntervalSec);

public sealed record HeartbeatPayload(
    double CpuPercent,
    double MemoryPercent,
    string? ConsoleUser,
    int IdleSeconds);

public sealed record StateSnapshotPayload(
    string AppVersion,
    string? ConsoleUser,
    LocalUsage? Local,
    IReadOnlyList<RemotePeer> RemoteSessions);

public sealed record LocalUsage(
    Guid OperatorId,
    string OperatorName,
    string Task,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpectedEnd,
    AuthLevel AuthLevel);

public sealed record LocalExtendedPayload(DateTimeOffset ExpectedEnd);

public sealed record LocalEndedPayload(LocalEndReason EndReason, DateTimeOffset EndedAt);

public sealed record RemotePeer(
    Guid Sid,
    Guid UserId,
    string UserName,
    string ClientIp,
    DateTimeOffset StartedAt);

public sealed record RemoteEndedPayload(Guid Sid, RemoteEndReason EndReason, DateTimeOffset EndedAt);

public sealed record BypassDetectedPayload(BypassKind Kind, string Detail);

public sealed record LocalForceEndPayload(string? Reason);

public sealed record UpdateAvailablePayload(
    ComponentKind Component,
    PackageKind Kind,
    string Version,
    string Url,
    string Sha256,
    string Signature);

/// <summary>Both null means "all devices".</summary>
public sealed record SubscribePayload(IReadOnlyList<Guid>? DeviceIds, Guid? GroupId);

public sealed record DeviceState(
    Guid Id,
    string Name,
    DeviceStatus Status,
    Guid? GroupId,
    string? GroupName,
    bool Online,
    LocalUsage? Local,
    IReadOnlyList<RemotePeer> RemotePeers,
    string? AppVersion,
    DateTimeOffset? LastSeenAt);
