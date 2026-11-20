using System.Globalization;
using System.Text;
using CoHost.Contracts;
using Xunit;

namespace CoHost.Protocol.Tests;

public sealed class ParseFailureTests
{
    private const string IdText = "11111111-1111-1111-1111-111111111111";
    private const string Secret = "example-agent-secret";
    private const string Token = "example.access.token";
    private const string Ts = "2026-11-19T05:00:00Z";

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static string Hello(string payload) =>
        $$$"""{"type":"hello","id":"{{{IdText}}}","ts":"{{{Ts}}}","payload":{{{payload}}}}""";

    public static TheoryData<string, string, string, string, string> LeakCases() => new()
    {
        // (1) broken literal in the middle of the hello payload, secret after it
        {
            "agent",
            Hello($$$"""{"deviceId":"{{{IdText}}}","installKind":fals,"agentSecret":"{{{Secret}}}","protocolVersion":"0.1","appVersion":"0.1.0"}"""),
            Secret, "hello", "$"
        },
        // (2) broken null literal in heartbeat
        {
            "agent",
            $$$"""{"type":"heartbeat","id":"{{{IdText}}}","ts":"{{{Ts}}}","payload":{"cpuPercent":1.5,"memoryPercent":2.5,"consoleUser":nul,"idleSeconds":7,"agentSecret":"{{{Secret}}}"}}""",
            Secret, "heartbeat", "$"
        },
        // (3) client hello, token after the broken literal
        {
            "client",
            Hello($$$"""{"installKind":nul,"accessToken":"{{{Token}}}","protocolVersion":"0.1","appVersion":"0.1.0"}"""),
            Token, "hello", "$"
        },
        // (4) unquoted secret value
        {
            "agent",
            Hello($$$"""{"deviceId":"{{{IdText}}}","agentSecret":{{{Secret}}},"protocolVersion":"0.1","appVersion":"0.1.0","installKind":"dev"}"""),
            Secret, "hello", "$"
        },
        // (5) invalid escape sequence
        {
            "agent",
            Hello($$$"""{"deviceId":"{{{IdText}}}","appVersion":"0.1\q","agentSecret":"{{{Secret}}}","protocolVersion":"0.1","installKind":"dev"}"""),
            Secret, "hello", "$"
        },
        // (6) payload before type and broken
        {
            "agent",
            $$$"""{"payload":{"x":fals,"agentSecret":"{{{Secret}}}"},"type":"hello","id":"{{{IdText}}}","ts":"{{{Ts}}}"}""",
            Secret, "<unknown>", "$"
        },
        // (7) type with an escaped newline, so it is not log-safe
        {
            "agent",
            $$$"""{"type":"bad type\nX","id":"{{{IdText}}}","ts":"{{{Ts}}}","payload":{"x":fals,"agentSecret":"{{{Secret}}}"}}""",
            Secret, "<invalid>", "$"
        },
        // (8) lone surrogate in type
        {
            "agent",
            $$$"""{"type":"\uD800","id":"{{{IdText}}}","ts":"{{{Ts}}}","payload":{"deviceId":"{{{IdText}}}","agentSecret":"{{{Secret}}}","protocolVersion":"0.1","appVersion":"0.1.0","installKind":"dev"}}""",
            Secret, "<unknown>", "$.type"
        },
    };

    [Theory]
    [MemberData(nameof(LeakCases))]
    public void Parse_failure_exposes_only_type_path_and_position(
        string channel, string json, string secret, string expectedType, string expectedPath)
    {
        var ex = Assert.Throws<ContractParseException>(() => Discriminators.Parse(channel, Utf8(json)));

        Assert.DoesNotContain(secret, ex.ToString());
        Assert.Null(ex.InnerException);
        Assert.Empty(ex.Data);
        Assert.Equal(expectedType, ex.MessageType);
        Assert.Equal(expectedPath, ex.Path);
        Assert.Equal(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Invalid '{expectedType}' message at {expectedPath} (line {ex.LineNumber}, byte {ex.BytePositionInLine})."),
            ex.Message);
    }

    public static TheoryData<string, string> UnknownTypeNames() => new()
    {
        { """
          "future message\nINJECT"
          """, "<invalid>" },
        { $"\"{new string('a', 65)}\"", "<invalid>" },
        { "\"Future.Message\"", "<invalid>" },
        { "\"future.v2Message\"", "future.v2Message" },
    };

    [Theory]
    [MemberData(nameof(UnknownTypeNames))]
    public void Unknown_type_name_is_log_safe(string typeJson, string expected)
    {
        var json = Utf8($$$"""{"type":{{{typeJson}}},"id":"{{{IdText}}}","ts":"{{{Ts}}}","payload":{}}""");

        var (message, unknown) = ContractJson.Parse<AgentMessage>(json);

        Assert.Null(message);
        Assert.Equal(expected, unknown);
    }

    public static TheoryData<string?, bool> TypeNames() => new()
    {
        { "hello", true },
        { "local.forceEnd", true },
        { "a" + new string('b', 63), true },
        { null, false },
        { "hello\n", false },
        { "Hello", false },
        { "hello..ack", false },
        { "a" + new string('b', 64), false },
    };

    [Theory]
    [MemberData(nameof(TypeNames))]
    public void Log_safe_type_name_rule(string? value, bool expected)
    {
        Assert.Equal(expected, ContractJson.IsLogSafeTypeName(value));
    }

    public static TheoryData<string?, bool> Paths() => new()
    {
        { "$", true },
        { "$.payload.remoteSessions[0].sid", true },
        { "$." + new string('a', 254), true },
        { null, false },
        { "$.payload.installKind\n", false },
        { "$['bad key']", false },
        { "$.payload[x]", false },
        { "$." + new string('a', 255), false },
    };

    [Theory]
    [MemberData(nameof(Paths))]
    public void Log_safe_path_rule(string? value, bool expected)
    {
        Assert.Equal(expected, ContractJson.IsLogSafePath(value));
    }
}
