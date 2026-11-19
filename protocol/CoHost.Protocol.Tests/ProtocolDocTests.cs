using System.Reflection;
using System.Text.Json;
using CoHost.Contracts;
using CoHost.Remote.Protocol;
using Xunit;

namespace CoHost.Protocol.Tests;

/// <summary>Fails when docs/PROTOCOL.md falls out of sync with the protocol code.</summary>
public sealed class ProtocolDocTests
{
    private static readonly Lazy<string> Doc = new(LoadDoc);

    private static string LoadDoc()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CoHost.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, $"CoHost.slnx not found above '{AppContext.BaseDirectory}'; cannot locate the repo root.");
        var path = Path.Combine(dir.FullName, "docs", "PROTOCOL.md");
        Assert.True(File.Exists(path), $"docs/PROTOCOL.md not found at '{path}'.");
        return File.ReadAllText(path);
    }

    private static void AssertAllBackticked(IEnumerable<string> names, string what)
    {
        var missing = names.Distinct().Where(n => !Doc.Value.Contains($"`{n}`", StringComparison.Ordinal)).Order().ToArray();
        Assert.True(missing.Length == 0, $"docs/PROTOCOL.md is missing {what}: {string.Join(", ", missing)}");
    }

    [Fact]
    public void All_json_discriminators_documented()
    {
        var discriminators = new[] { typeof(AgentMessage), typeof(ClientMessage), typeof(AdminMessage) }
            .SelectMany(b => Discriminators.Map(b).Values);
        AssertAllBackticked(discriminators, "message discriminators");
    }

    [Fact]
    public void All_frame_types_documented()
    {
        AssertAllBackticked(Enum.GetNames<FrameType>(), "FrameType names");
    }

    [Fact]
    public void All_enum_wire_values_documented()
    {
        var enums = typeof(ProtocolInfo).Assembly.GetExportedTypes().Where(t => t.IsEnum).ToArray();
        Assert.NotEmpty(enums);

        var wireValues = enums
            .SelectMany(t => Enum.GetValues(t).Cast<object>())
            .Select(v => JsonSerializer.Serialize(v, v.GetType(), ContractJson.Options).Trim('"'));
        AssertAllBackticked(wireValues, "enum wire values");
    }

    [Fact]
    public void Close_codes_documented()
    {
        var codes = typeof(WsCloseCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(int))
            .Select(f => ((int)f.GetRawConstantValue()!).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        Assert.NotEmpty(codes);

        var missing = codes.Where(c => !Doc.Value.Contains(c, StringComparison.Ordinal)).ToArray();
        Assert.True(missing.Length == 0, $"docs/PROTOCOL.md is missing close codes: {string.Join(", ", missing)}");
        Assert.Contains("v0.1 확장", Doc.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Rule_sections_exist()
    {
        var lines = Doc.Value.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Contains(lines, l => l.StartsWith("## installKind", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("## update.available", StringComparison.Ordinal));
    }

    [Fact]
    public void Channels_and_limits_documented()
    {
        string[] required =
        [
            "/ws/agent", "/ws/client", "/ws/admin", "1 MiB", "16 MiB", $"`{ProtocolInfo.Version}`",
        ];
        var missing = required.Where(r => !Doc.Value.Contains(r, StringComparison.Ordinal)).ToArray();
        Assert.True(missing.Length == 0, $"docs/PROTOCOL.md is missing: {string.Join(", ", missing)}");
    }
}
