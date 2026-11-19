using System.Text.Json.Serialization;

namespace CoHost.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(AdminSubscribe), "subscribe")]
[JsonDerivedType(typeof(AdminDeviceUpdated), "device.updated")]
public class AdminMessage : Envelope
{
}

/// <summary>Admin->S, type <c>subscribe</c>.</summary>
public sealed class AdminSubscribe : AdminMessage
{
    public required SubscribePayload Payload { get; init; }
}

/// <summary>S->Admin, type <c>device.updated</c>.</summary>
public sealed class AdminDeviceUpdated : AdminMessage
{
    public required DeviceState Payload { get; init; }
}
