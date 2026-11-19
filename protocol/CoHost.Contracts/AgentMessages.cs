using System.Text.Json.Serialization;

namespace CoHost.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(AgentHello), "hello")]
[JsonDerivedType(typeof(AgentHelloAck), "hello.ack")]
[JsonDerivedType(typeof(AgentHeartbeat), "heartbeat")]
[JsonDerivedType(typeof(AgentStateSnapshot), "state.snapshot")]
[JsonDerivedType(typeof(AgentLocalStarted), "local.started")]
[JsonDerivedType(typeof(AgentLocalExtended), "local.extended")]
[JsonDerivedType(typeof(AgentLocalEnded), "local.ended")]
[JsonDerivedType(typeof(AgentRemoteStarted), "remote.started")]
[JsonDerivedType(typeof(AgentRemoteEnded), "remote.ended")]
[JsonDerivedType(typeof(AgentBypassDetected), "bypass.detected")]
[JsonDerivedType(typeof(AgentLocalForceEnd), "local.forceEnd")]
[JsonDerivedType(typeof(AgentUpdateAvailable), "update.available")]
public class AgentMessage : Envelope
{
}

/// <summary>A->S, type <c>hello</c>.</summary>
public sealed class AgentHello : AgentMessage
{
    public required AgentHelloPayload Payload { get; init; }
}

/// <summary>S->A, type <c>hello.ack</c>.</summary>
public sealed class AgentHelloAck : AgentMessage
{
    public required HelloAckPayload Payload { get; init; }
}

/// <summary>A->S, type <c>heartbeat</c>.</summary>
public sealed class AgentHeartbeat : AgentMessage
{
    public required HeartbeatPayload Payload { get; init; }
}

/// <summary>A->S, type <c>state.snapshot</c>.</summary>
public sealed class AgentStateSnapshot : AgentMessage
{
    public required StateSnapshotPayload Payload { get; init; }
}

/// <summary>A->S, type <c>local.started</c>.</summary>
public sealed class AgentLocalStarted : AgentMessage
{
    public required LocalUsage Payload { get; init; }
}

/// <summary>A->S, type <c>local.extended</c>.</summary>
public sealed class AgentLocalExtended : AgentMessage
{
    public required LocalExtendedPayload Payload { get; init; }
}

/// <summary>A->S, type <c>local.ended</c>.</summary>
public sealed class AgentLocalEnded : AgentMessage
{
    public required LocalEndedPayload Payload { get; init; }
}

/// <summary>A->S, type <c>remote.started</c>.</summary>
public sealed class AgentRemoteStarted : AgentMessage
{
    public required RemotePeer Payload { get; init; }
}

/// <summary>A->S, type <c>remote.ended</c>.</summary>
public sealed class AgentRemoteEnded : AgentMessage
{
    public required RemoteEndedPayload Payload { get; init; }
}

/// <summary>A->S, type <c>bypass.detected</c>.</summary>
public sealed class AgentBypassDetected : AgentMessage
{
    public required BypassDetectedPayload Payload { get; init; }
}

/// <summary>S->A, type <c>local.forceEnd</c>.</summary>
public sealed class AgentLocalForceEnd : AgentMessage
{
    public required LocalForceEndPayload Payload { get; init; }
}

/// <summary>S->A, type <c>update.available</c>.</summary>
public sealed class AgentUpdateAvailable : AgentMessage
{
    public required UpdateAvailablePayload Payload { get; init; }
}
