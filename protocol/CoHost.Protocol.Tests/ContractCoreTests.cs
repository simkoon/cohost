using System.Text.Json;
using System.Text.Json.Serialization;
using CoHost.Contracts;
using Xunit;

namespace CoHost.Protocol.Tests;

public sealed class ContractCoreTests
{
    private sealed class ProbeMessage : Envelope;

    private const string Sha256Hex = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public void ProtocolInfo_version_is_major_dot_minor()
    {
        Assert.Equal("0.1", ProtocolInfo.Version);
        Assert.Equal(ProtocolInfo.Version, $"{ProtocolInfo.Major}.{ProtocolInfo.Minor}");
        Assert.Equal(15, ProtocolInfo.DefaultHeartbeatIntervalSec);
    }

    [Fact]
    public void Options_are_frozen_and_strict()
    {
        var o = ContractJson.Options;
        Assert.True(o.IsReadOnly);
        Assert.Equal(JsonNamingPolicy.CamelCase, o.PropertyNamingPolicy);
        Assert.Equal(JsonNumberHandling.Strict, o.NumberHandling);
        Assert.True(o.AllowOutOfOrderMetadataProperties);
        Assert.Equal(JsonIgnoreCondition.Never, o.DefaultIgnoreCondition);
        Assert.True(o.RespectRequiredConstructorParameters);
        Assert.True(o.RespectNullableAnnotations);
    }

    [Fact]
    public void ServerInfo_wire_shape()
    {
        var info = new ServerInfo("CoHost", "0.1.0", "0.1", Sha256Hex, DatabaseStatus.MigrationsPending, true);

        var json = JsonSerializer.Serialize(info, ContractJson.Options);

        using var doc = JsonDocument.Parse(json);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(
            ["appName", "serverVersion", "protocolVersion", "certSha256", "database", "defaultDbPassword"],
            keys);
        Assert.Equal("migrations_pending", doc.RootElement.GetProperty("database").GetString());

        var back = JsonSerializer.Deserialize<ServerInfo>(json, ContractJson.Options);
        Assert.Equal(info, back);
    }

    [Fact]
    public void Enum_integers_rejected()
    {
        var json = $$"""
            {"appName":"CoHost","serverVersion":"0.1.0","protocolVersion":"0.1","certSha256":"{{Sha256Hex}}","database":1,"defaultDbPassword":false}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ServerInfo>(json, ContractJson.Options));
    }

    [Fact]
    public void Parse_rejects_oversized()
    {
        var oversized = new byte[ContractJson.MaxMessageBytes + 1];

        Assert.Throws<InvalidDataException>(() => ContractJson.Parse<ProbeMessage>(oversized));
    }
}
