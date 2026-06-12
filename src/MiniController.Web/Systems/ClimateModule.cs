using MiniController.Core.Ac;
using MiniController.Web.Services;

namespace MiniController.Web.Systems;

/// <summary>
/// The mini-split, exposed as a system module. Wraps the existing DeviceManager —
/// the control page still drives DeviceManager directly; this adapter provides the
/// dashboard tile, the poll hook, and the rail/registry metadata.
/// </summary>
public sealed class ClimateModule : ISystemModule
{
    private readonly DeviceManager _manager;
    private readonly AppPreferences _prefs;

    public ClimateModule(DeviceManager manager, AppPreferences prefs)
    {
        _manager = manager;
        _prefs = prefs;
        _manager.Changed += () => Changed?.Invoke();
        _prefs.Changed += () => Changed?.Invoke();
    }

    public string Id => "climate";
    public string Name => "Climate";
    public string Route => "/system/climate";
    public string Accent => "var(--lcars-orange)";
    public int PollSeconds => Math.Max(5, _manager.Settings.PollSeconds);

    public event Action? Changed;

    public async Task PollAsync(CancellationToken ct)
    {
        if (!_manager.IsConfigured) return;
        await _manager.RefreshAsync(ct);
        await _manager.EvaluateRegulationAsync(ct);
    }

    public SystemTileState GetTile()
    {
        if (!_manager.IsConfigured)
            return new SystemTileState("Not set up", "Tap to configure", false, false);

        var s = _manager.Status;
        if (s is null)
            return new SystemTileState("—", "Connecting…", false, false);

        var headline = s.IndoorTemperature is { } t
            ? _prefs.Format(t)
            : (s.PowerOn ? "ON" : "OFF");
        // In Auto, the mode label hides whether it's heating or cooling — surface Action.
        var modeLabel = s.PowerOn && s.Mode == OperationalMode.Auto && s.Action is ClimateAction.Cooling or ClimateAction.Heating
            ? $"Auto ({s.Action})"
            : s.Mode.ToString();
        var detail = $"{modeLabel} · set {_prefs.Format(s.TargetTemperature)} · {(s.PowerOn ? "ON" : "OFF")}";
        return new SystemTileState(headline, detail, true, s.PowerOn);
    }
}
