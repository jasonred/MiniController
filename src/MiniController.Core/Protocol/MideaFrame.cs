using MiniController.Core.Crypto;

namespace MiniController.Core.Protocol;

/// <summary>
/// The innermost Midea appliance frame (0xAA … checksum), ported from frame.py.
/// Wraps a command payload with the 10-byte header + trailing checksum.
/// </summary>
public static class MideaFrame
{
    public const int HeaderLength = 10;
    public const byte AirConditioner = 0xAC;

    private static int _messageId;

    /// <summary>Two's-complement checksum over the frame (excluding the start byte).</summary>
    public static byte Checksum(ReadOnlySpan<byte> data)
    {
        var sum = 0;
        foreach (var b in data)
            sum += b;
        return (byte)((~sum + 1) & 0xFF);
    }

    private static byte NextMessageId()
    {
        _messageId = (_messageId + 1) & 0xFF;
        return (byte)_messageId;
    }

    /// <summary>
    /// Build a complete appliance frame for an AC command. Mirrors Command.tobytes():
    /// payload = command + messageId, then frame data = payload + crc8(payload).
    /// </summary>
    public static byte[] BuildAcFrame(byte frameType, ReadOnlySpan<byte> command)
    {
        // payload = command + message id
        var payload = new byte[command.Length + 1];
        command.CopyTo(payload);
        payload[^1] = NextMessageId();

        // data = payload + crc8(payload)
        var data = new byte[payload.Length + 1];
        payload.CopyTo(data, 0);
        data[^1] = Crc8.Calculate(payload);

        // frame = header(10) + data + checksum
        var frame = new byte[HeaderLength + data.Length + 1];
        frame[0] = 0xAA;
        frame[1] = (byte)(data.Length + HeaderLength);
        frame[2] = AirConditioner;
        // frame[8] = protocol version (0)
        frame[9] = frameType;
        data.CopyTo(frame, HeaderLength);
        frame[^1] = Checksum(frame.AsSpan(1, frame.Length - 2));
        return frame;
    }

    /// <summary>Validate a received frame's length, checksum, and device type.</summary>
    public static void Validate(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < HeaderLength)
            throw new InvalidOperationException($"Frame too short: {Convert.ToHexString(frame)}");

        var checksum = Checksum(frame[1..^1]);
        if (checksum != frame[^1])
            throw new InvalidOperationException(
                $"Frame failed checksum. Received 0x{frame[^1]:X2}, expected 0x{checksum:X2}.");

        if (frame[2] != AirConditioner)
            throw new InvalidOperationException(
                $"Unexpected device type 0x{frame[2]:X2} (expected 0x{AirConditioner:X2}).");
    }
}
