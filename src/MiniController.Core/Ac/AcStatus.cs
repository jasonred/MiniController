namespace MiniController.Core.Ac;

/// <summary>Decoded snapshot of the AC's reported state.</summary>
public sealed class AcStatus
{
    public bool PowerOn { get; init; }
    public double TargetTemperature { get; init; }
    public OperationalMode Mode { get; init; }

    /// <summary>What the unit is actually doing right now (cooling, heating, idle...).</summary>
    public ClimateAction Action { get; init; } = ClimateAction.Unknown;
    public int FanSpeed { get; init; }
    public Preset Preset { get; init; }
    public SwingMode Swing { get; init; }
    public bool Beeper { get; init; }
    public bool Turbo { get; init; }
    public bool Eco { get; init; }
    public bool Sleep { get; init; }
    public bool Fahrenheit { get; init; }
    public bool FollowMe { get; init; }
    public bool AuxHeat { get; init; }
    public bool FreezeProtection { get; init; }
    public bool DisplayOn { get; init; }
    public bool FilterAlert { get; init; }
    public double? IndoorTemperature { get; init; }
    public double? OutdoorTemperature { get; init; }
    public int ErrorCode { get; init; }
    public int TargetHumidity { get; init; }

    // Diagnostic / extra readouts (ESPHome sensors).
    public double? IndoorHumidity { get; init; }
    public double? PowerUsageW { get; init; }
    public double? WifiSignalDbm { get; init; }
    public double? UptimeDays { get; init; }
}
