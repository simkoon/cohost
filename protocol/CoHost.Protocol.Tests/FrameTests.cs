using CoHost.Remote.Protocol;
using Xunit;

namespace CoHost.Protocol.Tests;

public sealed class FrameTests
{
    [Fact]
    public void Ping_bytes_are_fixed()
    {
        using var stream = new MemoryStream();

        Frame.Write(stream, new Frame((byte)FrameType.Ping, 0, new byte[] { 1, 2, 3 }));

        Assert.Equal(new byte[] { 13, 0, 3, 0, 0, 0, 1, 2, 3 }, stream.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(70_000)]
    public async Task Round_trip_sync_and_async(int size)
    {
        var payload = new byte[size];
        for (var i = 0; i < size; i++) payload[i] = (byte)(i * 7);
        var frame = new Frame((byte)FrameType.Video, 0x81, payload);

        using var sync = new MemoryStream();
        Frame.Write(sync, frame);
        sync.Position = 0;
        var syncBack = Frame.Read(sync);

        using var async = new MemoryStream();
        await Frame.WriteAsync(async, frame, TestContext.Current.CancellationToken);
        Assert.Equal(sync.ToArray(), async.ToArray());
        async.Position = 0;
        var asyncBack = await Frame.ReadAsync(async, TestContext.Current.CancellationToken);

        foreach (var back in new[] { syncBack, asyncBack })
        {
            Assert.Equal(frame.Type, back.Type);
            Assert.Equal(frame.Flags, back.Flags);
            Assert.Equal(payload, back.Payload.ToArray());
        }
    }

    [Fact]
    public void Unknown_type_is_skipped()
    {
        using var stream = new MemoryStream();
        Frame.Write(stream, new Frame(200, 0, new byte[] { 1, 2, 3, 4, 5 }));
        Frame.Write(stream, new Frame((byte)FrameType.Pong, 0, ReadOnlyMemory<byte>.Empty));
        stream.Position = 0;

        var unknown = Frame.Read(stream);
        var next = Frame.Read(stream);

        Assert.Equal(200, unknown.Type);
        Assert.False(unknown.IsKnownType);
        Assert.Equal(5, unknown.Payload.Length);
        Assert.Equal((byte)FrameType.Pong, next.Type);
        Assert.True(next.IsKnownType);
    }

    [Fact]
    public void Oversized_header_rejected()
    {
        var header = new byte[FrameHeader.Size];
        header[0] = (byte)FrameType.Video;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(2), (uint)FrameHeader.MaxPayload + 1);
        using var stream = new MemoryStream(header);

        Assert.Throws<InvalidDataException>(() => Frame.Read(stream));
    }

    [Fact]
    public void Oversized_write_rejected()
    {
        using var stream = new MemoryStream();
        var frame = new Frame((byte)FrameType.Video, 0, new byte[FrameHeader.MaxPayload + 1]);

        Assert.Throws<ArgumentOutOfRangeException>(() => Frame.Write(stream, frame));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Truncated_stream_throws()
    {
        using var stream = new MemoryStream([(byte)FrameType.Video, 0, 10, 0, 0, 0, 1, 2]);

        Assert.Throws<EndOfStreamException>(() => Frame.Read(stream));
    }

    [Fact]
    public void FrameType_matches_spec()
    {
        var expected = new Dictionary<string, byte>
        {
            ["Hello"] = 1, ["HelloAck"] = 2, ["Video"] = 3, ["CursorShape"] = 4, ["CursorPos"] = 5,
            ["Mouse"] = 6, ["Key"] = 7, ["Sas"] = 8, ["SelectMonitor"] = 9, ["RequestKeyframe"] = 10,
            ["Peers"] = 11, ["Kicked"] = 12, ["Ping"] = 13, ["Pong"] = 14,
        };

        var actual = Enum.GetValues<FrameType>().ToDictionary(t => t.ToString(), t => (byte)t);

        Assert.Equal(14, actual.Count);
        Assert.Equal(expected.OrderBy(p => p.Key), actual.OrderBy(p => p.Key));
    }
}
