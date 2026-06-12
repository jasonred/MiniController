namespace MiniController.Core.Ac;

/// <summary>Midea appliance frame types.</summary>
public static class FrameType
{
    public const byte Query = 0x03;
    public const byte Control = 0x02;
}

/// <summary>Operating mode values as encoded in the state byte.</summary>
public enum OperationalMode
{
    Auto = 1,
    Cool = 2,
    Dry = 3,
    Heat = 4,
    FanOnly = 5,
}

/// <summary>
/// What the unit is currently doing (separate from the mode it's *set* to).
/// In Auto mode, Action reveals whether the compressor is heating or cooling.
/// </summary>
public enum ClimateAction
{
    Unknown = 0,
    Off,
    Idle,
    Cooling,
    Heating,
    Drying,
    Fan,
}

/// <summary>
/// Fan speed values. The unit accepts 0–100 plus 102 = Auto; these are the
/// named steps msmart exposes. Low/Medium/High map to the LED bars.
/// </summary>
public enum FanSpeed
{
    Auto = 102,
    Full = 100,
    High = 80,
    Medium = 60,
    Low = 40,
    Silent = 20,
}
