using System.Text.Json.Serialization;

namespace CoHost.Contracts;

/// <summary>
/// Common envelope of every WebSocket JSON message: <c>{ "type", "id", "ts", "payload" }</c>.
/// Channel base classes derive from this and carry the <c>type</c> discriminator.
/// </summary>
public abstract class Envelope
{
    [JsonPropertyOrder(-2)]
    public required Guid Id { get; init; }

    [JsonPropertyOrder(-1)]
    public required DateTimeOffset Ts { get; init; }
}
