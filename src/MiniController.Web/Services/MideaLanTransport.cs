using MiniController.Core.Ac;

namespace MiniController.Web.Services;

/// <summary>Transport for the stock Midea LAN protocol (token/key dongle). Wraps AcDevice.</summary>
public sealed class MideaLanTransport(string ip, int port, long deviceId, byte[] token, byte[] key)
    : IClimateTransport
{
    private readonly AcDevice _device = new(ip, port, deviceId, token, key);

    public Task<AcStatus> RefreshAsync(CancellationToken ct = default) => _device.RefreshAsync(ct);

    public Task<AcStatus> SetPowerAsync(bool on, CancellationToken ct = default) =>
        _device.Mutate(r => r.PowerOn = on, ct);

    public Task<AcStatus> SetModeAsync(OperationalMode mode, CancellationToken ct = default) =>
        _device.Mutate(r => { r.Mode = mode; r.PowerOn = true; }, ct);

    public Task<AcStatus> SetTemperatureAsync(double celsius, CancellationToken ct = default) =>
        _device.Mutate(r => r.TargetTemperature = Math.Clamp(celsius, 16, 30), ct);

    public Task<AcStatus> SetFanAsync(int fanSpeed, CancellationToken ct = default) =>
        _device.Mutate(r => r.FanSpeed = fanSpeed, ct);

    public Task<AcStatus> SetPresetAsync(Preset preset, CancellationToken ct = default) =>
        _device.Mutate(r =>
        {
            r.Eco = preset == Preset.Eco;
            r.Turbo = preset == Preset.Boost;
            r.Sleep = preset == Preset.Sleep;
        }, ct);

    public Task<AcStatus> SetSwingAsync(SwingMode swing, CancellationToken ct = default) =>
        _device.Mutate(r => r.SwingMode = swing switch
        {
            SwingMode.Both => 0xF,
            SwingMode.Vertical => 0xC,
            SwingMode.Horizontal => 0x3,
            _ => 0x0,
        }, ct);

    public Task<AcStatus> SetBeeperAsync(bool on, CancellationToken ct = default) =>
        _device.Mutate(r => r.BeepOn = on, ct);

    // Display toggle / swing step are ESPHome-only conveniences; the LAN path just re-reads.
    public Task<AcStatus> ToggleDisplayAsync(CancellationToken ct = default) => _device.RefreshAsync(ct);

    public Task<AcStatus> SwingStepAsync(CancellationToken ct = default) => _device.RefreshAsync(ct);

    public void Dispose() => _device.Dispose();
}
