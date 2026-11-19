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
}
