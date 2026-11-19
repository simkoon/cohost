using System.Buffers.Binary;

namespace CoHost.Remote.Protocol;

/// <summary>Binary frame types of the remote-screen channel (SPEC 5.2).</summary>
public enum FrameType : byte
{
    Hello = 1,
    HelloAck = 2,
    Video = 3,
    CursorShape = 4,
    CursorPos = 5,
    Mouse = 6,
    Key = 7,
    Sas = 8,
    SelectMonitor = 9,
    RequestKeyframe = 10,
    Peers = 11,
    Kicked = 12,
    Ping = 13,
    Pong = 14,
}

/// <summary>
/// One binary frame: <c>[type:u8][flags:u8][length:u32 LE][payload]</c>.
/// <see cref="Type"/> is a raw byte so frames of unknown type can still be read and skipped.
/// </summary>
public readonly record struct Frame(byte Type, byte Flags, ReadOnlyMemory<byte> Payload)
{
    public bool IsKnownType => Enum.IsDefined((FrameType)Type);

    public static void Write(Stream stream, in Frame frame)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> header = stackalloc byte[FrameHeader.Size];
        EncodeHeader(header, frame);
        stream.Write(header);
        stream.Write(frame.Payload.Span);
    }

    public static async ValueTask WriteAsync(Stream stream, Frame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[FrameHeader.Size];
        EncodeHeader(header, frame);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(frame.Payload, cancellationToken).ConfigureAwait(false);
    }

    /// <exception cref="InvalidDataException">The length field exceeds <see cref="FrameHeader.MaxPayload"/>.</exception>
    /// <exception cref="EndOfStreamException">The stream ended inside the header or payload.</exception>
    public static Frame Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> header = stackalloc byte[FrameHeader.Size];
        stream.ReadExactly(header);
        var length = DecodeHeader(header, out var type, out var flags);
        var payload = length == 0 ? [] : new byte[length];
        stream.ReadExactly(payload);
        return new Frame(type, flags, payload);
    }

    /// <exception cref="InvalidDataException">The length field exceeds <see cref="FrameHeader.MaxPayload"/>.</exception>
    /// <exception cref="EndOfStreamException">The stream ended inside the header or payload.</exception>
    public static async ValueTask<Frame> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[FrameHeader.Size];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = DecodeHeader(header, out var type, out var flags);
        var payload = length == 0 ? [] : new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return new Frame(type, flags, payload);
    }

    private static void EncodeHeader(Span<byte> header, in Frame frame)
    {
        if (frame.Payload.Length > FrameHeader.MaxPayload)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frame), frame.Payload.Length, $"Payload exceeds {FrameHeader.MaxPayload} bytes.");
        }

        header[0] = frame.Type;
        header[1] = frame.Flags;
        BinaryPrimitives.WriteUInt32LittleEndian(header[2..], (uint)frame.Payload.Length);
    }

    // The length is validated here, before any payload buffer is allocated.
    private static int DecodeHeader(ReadOnlySpan<byte> header, out byte type, out byte flags)
    {
        type = header[0];
        flags = header[1];
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header[2..]);
        if (length > FrameHeader.MaxPayload)
        {
            throw new InvalidDataException($"Frame payload length {length} exceeds {FrameHeader.MaxPayload} bytes.");
        }

        return (int)length;
    }
}
