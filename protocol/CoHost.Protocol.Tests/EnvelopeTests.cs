using System.Text;
using System.Text.Json;
using CoHost.Contracts;
using Xunit;

namespace CoHost.Protocol.Tests;

public sealed class EnvelopeTests
{
    private const string IdText = "11111111-1111-1111-1111-111111111111";

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    public static TheoryData<string> Names() => SampleMessages.Names();

    [Theory]
    [MemberData(nameof(Names))]
    public void Round_trip(string name)
    {
        var original = SampleMessages.ByName()[name];
        var channel = Discriminators.ChannelOf(original);

        var first = Discriminators.Serialize(original);
        var (message, unknown) = Discriminators.Parse(channel, first);

        Assert.Null(unknown);
        Assert.NotNull(message);
        Assert.Equal(original.GetType(), message.GetType());
        Assert.Equal(first, Discriminators.Serialize(message));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Envelope_shape(string name)
    {
        var json = Discriminators.Serialize(SampleMessages.ByName()[name]);

        using var doc = JsonDocument.Parse(json);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["type", "id", "ts", "payload"], keys);
    }

    [Fact]
    public void Unknown_type_is_reported_not_thrown()
    {
        var json = Utf8($$$"""{"type":"future.message","id":"{{{IdText}}}","ts":"2026-11-19T05:00:00Z","payload":{"x":1}}""");

        var (message, unknown) = ContractJson.Parse<AgentMessage>(json);

        Assert.Null(message);
        Assert.Equal("future.message", unknown);
    }

    [Fact]
    public void Missing_type_reported()
    {
        var json = Utf8($$$"""{"id":"{{{IdText}}}","ts":"2026-11-19T05:00:00Z","payload":{}}""");

        var (message, unknown) = ContractJson.Parse<AgentMessage>(json);

        Assert.Null(message);
        Assert.Equal("<missing>", unknown);
    }

    [Fact]
    public void Unknown_fields_ignored()
    {
        var json = Utf8("""
            {"type":"heartbeat","id":"11111111-1111-1111-1111-111111111111","ts":"2026-11-19T05:00:00Z","someFutureField":true,
             "payload":{"cpuPercent":1.5,"memoryPercent":2.5,"consoleUser":"operator01","idleSeconds":7,"someFutureField":{"a":1}}}
            """);

        var (message, unknown) = ContractJson.Parse<AgentMessage>(json);

        Assert.Null(unknown);
        var heartbeat = Assert.IsType<AgentHeartbeat>(message);
        Assert.Equal(new HeartbeatPayload(1.5, 2.5, "operator01", 7), heartbeat.Payload);
    }

    [Fact]
    public void Type_need_not_be_first()
    {
        var json = Utf8($$$"""
            {"payload":{"cpuPercent":1.5,"memoryPercent":2.5,"consoleUser":null,"idleSeconds":7},
             "ts":"2026-11-19T05:00:00Z","id":"{{{IdText}}}","type":"heartbeat"}
            """);

        var (message, unknown) = ContractJson.Parse<AgentMessage>(json);

        Assert.Null(unknown);
        Assert.IsType<AgentHeartbeat>(message);
    }

    [Fact]
    public void Timestamps_accept_Z_and_offset()
    {
        static DateTimeOffset TsOf(string ts) =>
            ContractJson.Parse<AgentMessage>(Utf8($$$"""
                {"type":"local.extended","id":"{{{IdText}}}","ts":"{{{ts}}}","payload":{"expectedEnd":"2026-11-19T06:00:00Z"}}
                """)).Message!.Ts;

        var z = TsOf("2026-11-19T05:00:00Z");
        var offset = TsOf("2026-11-19T14:00:00+09:00");

        Assert.Equal(z, offset);
        Assert.Equal(TimeSpan.FromHours(9), offset.Offset);
    }

    [Fact]
    public void Nulls_are_written()
    {
        var heartbeat = new AgentHeartbeat
        {
            Id = SampleMessages.MessageId,
            Ts = SampleMessages.Ts,
            Payload = new(1, 2, null, 3),
        };

        var json = Encoding.UTF8.GetString(ContractJson.Serialize(heartbeat));

        Assert.Contains("\"consoleUser\":null", json);
    }

    [Fact]
    public void Enums_are_snake_case()
    {
        Assert.Equal("\"local_kick\"", JsonSerializer.Serialize(RemoteEndReason.LocalKick, ContractJson.Options));
        Assert.Equal("\"dev\"", JsonSerializer.Serialize(InstallKind.Dev, ContractJson.Options));
        Assert.Equal("\"none\"", JsonSerializer.Serialize(AuthLevel.None, ContractJson.Options));
    }

    [Fact]
    public void Malformed_json_throws_JsonException()
    {
        Assert.ThrowsAny<JsonException>(() => ContractJson.Parse<AgentMessage>(Utf8("{\"type\":")));
    }

    [Fact]
    public void Oversized_throws_InvalidDataException()
    {
        var oversized = new byte[ContractJson.MaxMessageBytes + 1];

        Assert.Throws<InvalidDataException>(() => ContractJson.Parse<AgentMessage>(oversized));
    }

    [Fact]
    public void Serialize_concrete_includes_type()
    {
        var hello = new AgentHello
        {
            Id = SampleMessages.MessageId,
            Ts = SampleMessages.Ts,
            Payload = new(Guid.Empty, "example-agent-secret", ProtocolInfo.Version, "0.1.0", InstallKind.Dev),
        };

        using var doc = JsonDocument.Parse(ContractJson.Serialize(hello));

        Assert.Equal("hello", doc.RootElement.GetProperty("type").GetString());
    }

    public static TheoryData<string, string> InvalidAgentHelloPayloads() => new()
    {
        { "empty payload object", "{}" },
        { "null payload", "null" },
        {
            "installKind missing",
            """{"deviceId":"11111111-1111-1111-1111-111111111111","agentSecret":"example-agent-secret","protocolVersion":"0.1","appVersion":"0.1.0"}"""
        },
        {
            "agentSecret null",
            """{"deviceId":"11111111-1111-1111-1111-111111111111","agentSecret":null,"protocolVersion":"0.1","appVersion":"0.1.0","installKind":"dev"}"""
        },
    };

    [Theory]
    [MemberData(nameof(InvalidAgentHelloPayloads))]
    public void Invalid_agent_hello_is_a_parse_failure(string caseName, string payload)
    {
        var json = Utf8($$$"""{"type":"hello","id":"{{{IdText}}}","ts":"2026-11-19T05:00:00Z","payload":{{{payload}}}}""");

        Assert.ThrowsAny<JsonException>(() => ContractJson.Parse<AgentMessage>(json));
        Assert.False(string.IsNullOrEmpty(caseName));
    }

    [Fact]
    public void Parse_failure_message_has_no_secret()
    {
        var json = Utf8($$$"""
            {"type":"hello","id":"{{{IdText}}}","ts":"2026-11-19T05:00:00Z",
             "payload":{"deviceId":"{{{IdText}}}","agentSecret":"example-agent-secret","protocolVersion":"0.1","appVersion":"0.1.0"}}
            """);

        var ex = Assert.ThrowsAny<JsonException>(() => ContractJson.Parse<AgentMessage>(json));

        Assert.DoesNotContain("example-agent-secret", ex.Message);
        Assert.DoesNotContain("example-agent-secret", ex.InnerException?.Message ?? string.Empty);
        Assert.Contains("installKind", ex.Message);
    }

    [Fact]
    public void Client_hello_without_installKind_is_a_parse_failure()
    {
        var json = Utf8($$$"""
            {"type":"hello","id":"{{{IdText}}}","ts":"2026-11-19T05:00:00Z",
             "payload":{"accessToken":"example.access.token","protocolVersion":"0.1","appVersion":"0.1.0"}}
            """);

        Assert.ThrowsAny<JsonException>(() => ContractJson.Parse<ClientMessage>(json));
    }

    [Fact]
    public void Nullable_field_key_must_be_present()
    {
        var withoutKey = Utf8($$$"""
            {"type":"heartbeat","id":"{{{IdText}}}","ts":"2026-11-19T05:00:00Z",
             "payload":{"cpuPercent":1.5,"memoryPercent":2.5,"idleSeconds":7}}
            """);
        var withNull = Utf8($$$"""
            {"type":"heartbeat","id":"{{{IdText}}}","ts":"2026-11-19T05:00:00Z",
             "payload":{"cpuPercent":1.5,"memoryPercent":2.5,"consoleUser":null,"idleSeconds":7}}
            """);

        Assert.ThrowsAny<JsonException>(() => ContractJson.Parse<AgentMessage>(withoutKey));

        var (message, unknown) = ContractJson.Parse<AgentMessage>(withNull);
        Assert.Null(unknown);
        var heartbeat = Assert.IsType<AgentHeartbeat>(message);
        Assert.Null(heartbeat.Payload.ConsoleUser);
    }

    [Fact]
    public void Serialize_rejects_null_in_non_nullable_member()
    {
        var hello = new AgentHello
        {
            Id = SampleMessages.MessageId,
            Ts = SampleMessages.Ts,
            Payload = null!,
        };

        Assert.ThrowsAny<JsonException>(() => ContractJson.Serialize(hello));
    }
}
