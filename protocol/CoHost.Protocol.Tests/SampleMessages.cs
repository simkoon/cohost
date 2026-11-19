using CoHost.Contracts;
using Xunit;

namespace CoHost.Protocol.Tests;

/// <summary>One fully populated sample per message. Generic example values only.</summary>
public static class SampleMessages
{
    public static readonly Guid MessageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly DateTimeOffset Ts = new(2026, 11, 19, 5, 0, 0, TimeSpan.Zero);

    private static readonly Guid DeviceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OperatorId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UserId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Sid = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid GroupId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static readonly DateTimeOffset Started = new(2026, 11, 19, 5, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Expected = new(2026, 11, 19, 6, 0, 0, TimeSpan.Zero);

    private static LocalUsage Local() =>
        new(OperatorId, "김철수", "레시피 수정", Started, Expected, AuthLevel.Pin);

    private static RemotePeer Peer() =>
        new(Sid, UserId, "홍길동", "192.0.2.10", Started);

    private static UpdateAvailablePayload Update(ComponentKind component) =>
        new(component, PackageKind.Msi, "0.1.1", "https://cohost.local:8443/packages/agent-0.1.1.msi",
            "0000000000000000000000000000000000000000000000000000000000000000", "example-signature");

    private static HelloAckPayload Ack() =>
        new(Ts, ProtocolInfo.Version, ProtocolInfo.Version, ProtocolInfo.DefaultHeartbeatIntervalSec);

    private static DeviceState Device() =>
        new(DeviceId, "EQ-001", DeviceStatus.Approved, GroupId, "라인 A", true, Local(), [Peer()], "0.1.0", Ts);

    public static IReadOnlyList<AgentMessage> AgentAll() =>
    [
        new AgentHello { Id = MessageId, Ts = Ts, Payload = new(DeviceId, "example-agent-secret", ProtocolInfo.Version, "0.1.0", InstallKind.Msi) },
        new AgentHelloAck { Id = MessageId, Ts = Ts, Payload = Ack() },
        new AgentHeartbeat { Id = MessageId, Ts = Ts, Payload = new(12.5, 40.25, null, 30) },
        new AgentStateSnapshot { Id = MessageId, Ts = Ts, Payload = new("0.1.0", "operator01", Local(), [Peer()]) },
        new AgentLocalStarted { Id = MessageId, Ts = Ts, Payload = Local() },
        new AgentLocalExtended { Id = MessageId, Ts = Ts, Payload = new(Expected.AddHours(1)) },
        new AgentLocalEnded { Id = MessageId, Ts = Ts, Payload = new(LocalEndReason.Manual, Expected) },
        new AgentRemoteStarted { Id = MessageId, Ts = Ts, Payload = Peer() },
        new AgentRemoteEnded { Id = MessageId, Ts = Ts, Payload = new(Sid, RemoteEndReason.LocalKick, Expected) },
        new AgentBypassDetected { Id = MessageId, Ts = Ts, Payload = new(BypassKind.Rdp, "example-detail") },
        new AgentLocalForceEnd { Id = MessageId, Ts = Ts, Payload = new((string?)null) },
        new AgentUpdateAvailable { Id = MessageId, Ts = Ts, Payload = Update(ComponentKind.Agent) },
    ];

    public static IReadOnlyList<ClientMessage> ClientAll() =>
    [
        new ClientHello { Id = MessageId, Ts = Ts, Payload = new("example.access.token", ProtocolInfo.Version, "0.1.0", InstallKind.Zip) },
        new ClientHelloAck { Id = MessageId, Ts = Ts, Payload = Ack() },
        new ClientSubscribe { Id = MessageId, Ts = Ts, Payload = new(null, GroupId) },
        new ClientUpdateAvailable { Id = MessageId, Ts = Ts, Payload = Update(ComponentKind.Client) },
        new ClientDeviceUpdated { Id = MessageId, Ts = Ts, Payload = Device() },
    ];

    public static IReadOnlyList<AdminMessage> AdminAll() =>
    [
        new AdminSubscribe { Id = MessageId, Ts = Ts, Payload = new([DeviceId], null) },
        new AdminDeviceUpdated { Id = MessageId, Ts = Ts, Payload = Device() },
    ];

    /// <summary>All 19 samples keyed by <c>channel.discriminator</c>, e.g. <c>agent.hello.ack</c>.</summary>
    public static IReadOnlyDictionary<string, Envelope> ByName()
    {
        var all = new Dictionary<string, Envelope>();
        foreach (var m in AgentAll()) all[$"agent.{Discriminators.Of(m)}"] = m;
        foreach (var m in ClientAll()) all[$"client.{Discriminators.Of(m)}"] = m;
        foreach (var m in AdminAll()) all[$"admin.{Discriminators.Of(m)}"] = m;
        return all;
    }

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var name in ByName().Keys) data.Add(name);
        return data;
    }
}
