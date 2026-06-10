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

    public void Dispose() => _device.Dispose();
}
