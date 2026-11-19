using System.Text.Json.Serialization;

namespace CoHost.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(ClientHello), "hello")]
[JsonDerivedType(typeof(ClientHelloAck), "hello.ack")]
[JsonDerivedType(typeof(ClientSubscribe), "subscribe")]
[JsonDerivedType(typeof(ClientUpdateAvailable), "update.available")]
[JsonDerivedType(typeof(ClientDeviceUpdated), "device.updated")]
public class ClientMessage : Envelope
{
}

/// <summary>C->S, type <c>hello</c>.</summary>
public sealed class ClientHello : ClientMessage
{
    public required ClientHelloPayload Payload { get; init; }
}

/// <summary>S->C, type <c>hello.ack</c>.</summary>
public sealed class ClientHelloAck : ClientMessage
{
    public required HelloAckPayload Payload { get; init; }
}

/// <summary>C->S, type <c>subscribe</c>.</summary>
public sealed class ClientSubscribe : ClientMessage
{
    public required SubscribePayload Payload { get; init; }
}

/// <summary>S->C, type <c>update.available</c>.</summary>
public sealed class ClientUpdateAvailable : ClientMessage
{
    public required UpdateAvailablePayload Payload { get; init; }
}

/// <summary>S->C, type <c>device.updated</c>.</summary>
public sealed class ClientDeviceUpdated : ClientMessage
{
    public required DeviceState Payload { get; init; }
}
