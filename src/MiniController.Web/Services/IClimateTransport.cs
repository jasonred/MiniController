using MiniController.Core.Ac;

namespace MiniController.Web.Services;

/// <summary>
/// A way to read and command the mini-split. One implementation talks the Midea
/// LAN protocol directly (stock-dongle path); another talks the SLWF's ESPHome
/// web server. DeviceManager picks one based on settings.
/// Each setter performs the change and returns the resulting state.
/// </summary>
public interface IClimateTransport : IDisposable
{
    Task<AcStatus> RefreshAsync(CancellationToken ct = default);
    Task<AcStatus> SetPowerAsync(bool on, CancellationToken ct = default);
    Task<AcStatus> SetModeAsync(OperationalMode mode, CancellationToken ct = default);
    Task<AcStatus> SetTemperatureAsync(double celsius, CancellationToken ct = default);
    Task<AcStatus> SetFanAsync(int fanSpeed, CancellationToken ct = default);
    Task<AcStatus> SetPresetAsync(Preset preset, CancellationToken ct = default);
    Task<AcStatus> SetSwingAsync(SwingMode swing, CancellationToken ct = default);
    Task<AcStatus> SetBeeperAsync(bool on, CancellationToken ct = default);
    Task<AcStatus> ToggleDisplayAsync(CancellationToken ct = default);
    Task<AcStatus> SwingStepAsync(CancellationToken ct = default);
}
