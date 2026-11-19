using CoHost.Contracts;
using Xunit;

namespace CoHost.Protocol.Tests;

public sealed class SpecCoverageTests
{
    private static string[] Set(Type channelBase) => [.. Discriminators.Map(channelBase).Values.Order()];

    [Fact]
    public void Agent_channel_types()
    {
        string[] expected =
        [
            "hello", "hello.ack", "heartbeat", "state.snapshot", "local.started", "local.extended",
            "local.ended", "remote.started", "remote.ended", "bypass.detected", "local.forceEnd", "update.available",
        ];
        Assert.Equal([.. expected.Order()], Set(typeof(AgentMessage)));
    }

    [Fact]
    public void Client_channel_types()
    {
        string[] expected = ["hello", "hello.ack", "subscribe", "update.available", "device.updated"];
        Assert.Equal([.. expected.Order()], Set(typeof(ClientMessage)));
    }

    [Fact]
    public void Admin_channel_types()
    {
        string[] expected = ["subscribe", "device.updated"];
        Assert.Equal([.. expected.Order()], Set(typeof(AdminMessage)));
    }

    [Fact]
    public void Bases_are_concrete()
    {
        Assert.False(typeof(AgentMessage).IsAbstract);
        Assert.False(typeof(ClientMessage).IsAbstract);
        Assert.False(typeof(AdminMessage).IsAbstract);
    }
}
