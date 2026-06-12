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
/// Fan speeds matching what the ESPHome dongle exposes: AUTO / LOW / MEDIUM / HIGH
/// plus the custom "silent" and "turbo" modes. Numeric values double as the Midea
/// fan-speed byte for the legacy LAN path (Auto = 102, Turbo = full 100).
/// </summary>
public enum FanSpeed
{
    Silent = 20,
    Low = 40,
    Medium = 60,
    High = 80,
    Turbo = 100,
    Auto = 102,
}

/// <summary>Climate presets (comfort/energy profiles): the ESPHome NONE/BOOST/ECO/SLEEP set.</summary>
public enum Preset
{
    None,
    Boost,
    Eco,
    Sleep,
}

/// <summary>Louver swing modes the unit exposes.</summary>
public enum SwingMode
{
    Off,
    Both,
    Vertical,
    Horizontal,
}
