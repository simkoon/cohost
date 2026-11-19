using System.Reflection;
using System.Text.Json.Serialization;
using CoHost.Contracts;

namespace CoHost.Protocol.Tests;

/// <summary>Reads the <c>[JsonDerivedType]</c> discriminators from channel bases (test helper).</summary>
public static class Discriminators
{
    public static IReadOnlyDictionary<Type, string> Map(Type channelBase) =>
        channelBase.GetCustomAttributes<JsonDerivedTypeAttribute>()
            .ToDictionary(a => a.DerivedType, a => (string)a.TypeDiscriminator!);

    public static string Of(Envelope message)
    {
        var baseType = message switch
        {
            AgentMessage => typeof(AgentMessage),
            ClientMessage => typeof(ClientMessage),
            AdminMessage => typeof(AdminMessage),
            _ => throw new ArgumentException("not a channel message"),
        };
        return Map(baseType)[message.GetType()];
    }

    public static (Envelope? Message, string? UnknownType) Parse(string channel, byte[] utf8) => channel switch
    {
        "agent" => ContractJson.Parse<AgentMessage>(utf8),
        "client" => ContractJson.Parse<ClientMessage>(utf8),
        "admin" => ContractJson.Parse<AdminMessage>(utf8),
        _ => throw new ArgumentException(channel),
    };

    public static byte[] Serialize(Envelope message) => message switch
    {
        AgentMessage a => ContractJson.Serialize(a),
        ClientMessage c => ContractJson.Serialize(c),
        AdminMessage d => ContractJson.Serialize(d),
        _ => throw new ArgumentException("not a channel message"),
    };

    public static string ChannelOf(Envelope message) => message switch
    {
        AgentMessage => "agent",
        ClientMessage => "client",
        _ => "admin",
    };
}
