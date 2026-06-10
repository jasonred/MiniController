using MiniController.Core.Protocol;

namespace MiniController.Core.Ac;

/// <summary>Mutable description of a desired AC state, encoded into a 0x40 control frame.</summary>
public sealed class SetStateRequest
{
    public bool BeepOn { get; set; } = true;
    public bool PowerOn { get; set; }
    public double TargetTemperature { get; set; } = 22.0;
    public OperationalMode Mode { get; set; } = OperationalMode.Auto;
    public int FanSpeed { get; set; } = (int)Ac.FanSpeed.Auto;
    public bool Eco { get; set; }
    public int SwingMode { get; set; }
    public bool Turbo { get; set; }
    public bool Fahrenheit { get; set; } = true;
    public bool Sleep { get; set; }
    public bool FreezeProtection { get; set; }
    public bool FollowMe { get; set; }
    public int TargetHumidity { get; set; } = 40;

    /// <summary>Seed a request from a reported status so unchanged fields are preserved.</summary>
    public static SetStateRequest FromStatus(AcStatus s) => new()
    {
        PowerOn = s.PowerOn,
        TargetTemperature = s.TargetTemperature,
        Mode = s.Mode,
        FanSpeed = s.FanSpeed,
        Eco = s.Eco,
        SwingMode = s.SwingMode,
        Turbo = s.Turbo,
        Fahrenheit = s.Fahrenheit,
        Sleep = s.Sleep,
        FreezeProtection = s.FreezeProtection,
        FollowMe = s.FollowMe,
        TargetHumidity = s.TargetHumidity,
    };
}

/// <summary>Builds the appliance command frames, ported from device/AC/command.py.</summary>
public static class AcCommands
{
    private const byte ControlSource = 0x2; // App control

    /// <summary>0x41 query for basic state (indoor temperature variant).</summary>
    public static byte[] GetState()
    {
        ReadOnlySpan<byte> payload =
        [
            0x41,
            0x81, 0x00, 0xFF, 0x03, 0xFF, 0x00,
            0x02, // temperature type: indoor
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x03,
        ];
        return MideaFrame.BuildAcFrame(FrameType.Query, payload);
    }

    /// <summary>0x40 control frame setting power/mode/temp/fan and the common flags.</summary>
    public static byte[] SetState(SetStateRequest r)
    {
        var beep = r.BeepOn ? 0x40 : 0;
        var power = r.PowerOn ? 0x1 : 0;

        var integral = (int)Math.Floor(r.TargetTemperature);
        var fractional = r.TargetTemperature - integral;

        int temperature, temperatureAlt;
        if (integral is >= 17 and <= 30)
        {
            temperature = (integral - 16) & 0xF;
            temperatureAlt = 0;
        }
        else
        {
            temperature = 0;
            temperatureAlt = (integral - 12) & 0x1F;
        }

        if (fractional > 0)
            temperature |= 0x10; // half-degree bit

        var mode = ((int)r.Mode & 0x7) << 5;
        var swingMode = 0x30 | (r.SwingMode & 0x3F);

        var eco = r.Eco ? 0x80 : 0;
        var sleep = r.Sleep ? 0x01 : 0;
        var turbo = r.Turbo ? 0x02 : 0;
        var turboAlt = r.Turbo ? 0x20 : 0;
        var fahrenheit = r.Fahrenheit ? 0x04 : 0;
        var followMe = r.FollowMe ? 0x80 : 0;
        var humidity = r.TargetHumidity & 0x7F;
        var freezeProtect = r.FreezeProtection ? 0x80 : 0;

        ReadOnlySpan<byte> payload =
        [
            0x40,
            (byte)(ControlSource | beep | power),
            (byte)(temperature | mode),
            (byte)r.FanSpeed,
            0x7F, 0x7F, 0x00,            // timer
            (byte)swingMode,
            (byte)(followMe | turboAlt),
            (byte)eco,                   // eco | purifier | aux heat
            (byte)(sleep | turbo | fahrenheit),
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00,
            (byte)temperatureAlt,
            (byte)humidity,
            0x00,
            (byte)freezeProtect,
            0x00,                        // independent aux heat
            0x00,
        ];
        return MideaFrame.BuildAcFrame(FrameType.Control, payload);
    }
}
