using MiniController.Core.Protocol;

namespace MiniController.Core.Ac;

/// <summary>Parses an appliance response frame into an <see cref="AcStatus"/>.</summary>
public static class AcStateParser
{
    private const byte ResponseState = 0xC0;

    /// <summary>True when the frame is a state (0xC0) response we can decode.</summary>
    public static bool TryParse(byte[] frame, out AcStatus? status)
    {
        status = null;
        MideaFrame.Validate(frame);

        // Response payload excludes the 10-byte header and the trailing CRC + checksum.
        if (frame.Length < MideaFrame.HeaderLength + 2)
            return false;
        var payload = frame.AsSpan(MideaFrame.HeaderLength, frame.Length - MideaFrame.HeaderLength - 2);

        if (payload.Length == 0 || payload[0] != ResponseState || payload.Length < 17)
            return false;

        status = Parse(payload);
        return true;
    }

    private static AcStatus Parse(ReadOnlySpan<byte> p)
    {
        var fahrenheit = (p[10] & 0x4) != 0;

        var target = (p[2] & 0xF) + 16.0;
        if ((p[2] & 0x10) != 0) target += 0.5;

        var targetAlt = p[13] & 0x1F;
        if (targetAlt != 0)
        {
            target = targetAlt + 12;
            if ((p[2] & 0x10) != 0) target += 0.5;
        }

        var turbo = (p[8] & 0x20) != 0 || (p[10] & 0x2) != 0;

        return new AcStatus
        {
            PowerOn = (p[1] & 0x1) != 0,
            TargetTemperature = target,
            Mode = (OperationalMode)((p[2] >> 5) & 0x7),
            FanSpeed = p[3] & 0x7F,
            Preset = turbo ? Preset.Boost
                : (p[9] & 0x10) != 0 ? Preset.Eco
                : (p[10] & 0x1) != 0 ? Preset.Sleep
                : Preset.None,
            Swing = (p[7] & 0xF) == 0 ? SwingMode.Off : SwingMode.Both,
            Turbo = turbo,
            FollowMe = (p[8] & 0x80) != 0,
            Eco = (p[9] & 0x10) != 0,
            AuxHeat = (p[9] & 0x08) != 0,
            Sleep = (p[10] & 0x1) != 0,
            Fahrenheit = fahrenheit,
            IndoorTemperature = ParseTemperature(p[11], (p[15] & 0xF) / 10.0, fahrenheit),
            OutdoorTemperature = ParseTemperature(p[12], (p[15] >> 4) / 10.0, fahrenheit),
            FilterAlert = (p[13] & 0x20) != 0,
            DisplayOn = p[14] != 0x70,
            ErrorCode = p[16],
            TargetHumidity = p.Length >= 20 ? p[19] & 0x7F : 0,
            FreezeProtection = p.Length >= 22 && (p[21] & 0x80) != 0,
        };
    }

    private static double? ParseTemperature(int data, double decimals, bool fahrenheit)
    {
        if (data == 0xFF)
            return null;

        var temperature = (data - 50) / 2.0;

        if (!fahrenheit && decimals > 0)
            return (int)temperature + (temperature >= 0 ? decimals : -decimals);

        if (decimals >= 0.5)
            return (int)temperature + (temperature >= 0 ? 0.5 : -0.5);

        return temperature;
    }
}
