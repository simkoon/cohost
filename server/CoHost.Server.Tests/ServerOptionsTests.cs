using Microsoft.Extensions.Configuration;
using Xunit;

namespace CoHost.Server.Tests;

public class ServerOptionsTests
{
    private static ServerOptions Build(params (string Key, string Value)[] values)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
        return ServerOptions.FromConfiguration(config);
    }

    [Fact]
    public void Defaults()
    {
        var o = Build();
        Assert.Equal(8443, o.Port);
        Assert.Equal("/data", o.DataDir);
        Assert.Empty(o.Hostnames);
        Assert.False(o.UsesDefaultDbPassword);
    }

    [Fact]
    public void Port_override_and_validation()
    {
        Assert.Equal(9443, Build(("COHOST_PORT", "9443")).Port);
        var zero = Assert.Throws<InvalidOperationException>(() => Build(("COHOST_PORT", "0")));
        Assert.Contains("COHOST_PORT", zero.Message);
        var big = Assert.Throws<InvalidOperationException>(() => Build(("COHOST_PORT", "70000")));
        Assert.Contains("COHOST_PORT", big.Message);
    }

    [Fact]
    public void Hostnames_parsed()
    {
        var o = Build(("COHOST_HOSTNAMES", " cohost.local , 192.0.2.10 ,,"));
        Assert.Equal(new[] { "cohost.local", "192.0.2.10" }, o.Hostnames);
    }

    [Fact]
    public void Default_password_detected()
    {
        Assert.True(Build(("COHOST_DB", $"Host=postgres;Database=cohost;Username=cohost;Password={ServerOptions.DefaultDbPassword}")).UsesDefaultDbPassword);
        Assert.False(Build(("COHOST_DB", "Host=postgres;Database=cohost;Username=cohost;Password=s3cret-example")).UsesDefaultDbPassword);
    }
}
