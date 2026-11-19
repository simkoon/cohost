using System.Text.Json;

namespace CoHost.Contracts;

public static partial class ContractJson
{
    // Each overload serializes with the channel base as the static type, so the "type"
    // discriminator is always written even when a concrete message is passed.
    public static byte[] Serialize(AgentMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes<AgentMessage>(message, Options);

    public static byte[] Serialize(ClientMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes<ClientMessage>(message, Options);

    public static byte[] Serialize(AdminMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes<AdminMessage>(message, Options);
}
