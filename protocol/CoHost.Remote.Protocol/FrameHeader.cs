namespace CoHost.Remote.Protocol;

/// <summary>
/// Binary remote-screen frame header: <c>[type:u8][flags:u8][length:u32 LE]</c>.
/// </summary>
public static class FrameHeader
{
    public const int Size = 6;

    /// <summary>Largest accepted payload (16 MiB).</summary>
    public const int MaxPayload = 16 * 1024 * 1024;
}
