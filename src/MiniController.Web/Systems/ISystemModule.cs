namespace MiniController.Web.Systems;

/// <summary>At-a-glance state for a system's dashboard tile.</summary>
/// <param name="Headline">Big value, e.g. "72°F" or "ON".</param>
/// <param name="Detail">Secondary line, e.g. "Cool · set 70°F".</param>
/// <param name="Online">True if the system is reachable/configured.</param>
/// <param name="Active">True if the system is currently doing something (powered on, running).</param>
public readonly record struct SystemTileState(string Headline, string Detail, bool Online, bool Active);

/// <summary>
/// A controllable system surfaced in the app (climate, lighting, etc.).
/// Implementations are DI-registered as ISystemModule; the rail, dashboard,
/// and poller all build themselves from the registered set.
/// </summary>
public interface ISystemModule
{
    /// <summary>Stable id, e.g. "climate".</summary>
    string Id { get; }

    /// <summary>Display name for rail/tile, e.g. "Climate".</summary>
    string Name { get; }

    /// <summary>Route to this system's control page, e.g. "/system/climate".</summary>
    string Route { get; }

    /// <summary>LCARS accent (a CSS color or var), e.g. "var(--lcars-orange)".</summary>
    string Accent { get; }

    /// <summary>How often the poller should refresh this system, in seconds.</summary>
    int PollSeconds { get; }

    /// <summary>Current summary for the dashboard tile.</summary>
    SystemTileState GetTile();

    /// <summary>Refresh live state. No-op for systems that don't poll.</summary>
    Task PollAsync(CancellationToken ct);

    /// <summary>Raised when this system's state changes, so the UI can re-render.</summary>
    event Action? Changed;
}
