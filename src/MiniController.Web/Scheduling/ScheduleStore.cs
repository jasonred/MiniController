using System.Text.Json;

namespace MiniController.Web.Scheduling;

/// <summary>Persists climate schedule entries to schedules.json and exposes CRUD.</summary>
public sealed class ScheduleStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger<ScheduleStore> _logger;
    private readonly object _sync = new();
    private List<ScheduleEntry> _entries = [];

    public event Action? Changed;

    public ScheduleStore(IWebHostEnvironment env, ILogger<ScheduleStore> logger)
    {
        _logger = logger;
        _path = Path.Combine(env.ContentRootPath, "schedules.json");
        Load();
    }

    public IReadOnlyList<ScheduleEntry> Entries
    {
        get { lock (_sync) return _entries.ToList(); }
    }

    public void Save(ScheduleEntry entry)
    {
        lock (_sync)
        {
            var idx = _entries.FindIndex(e => e.Id == entry.Id);
            if (idx >= 0) _entries[idx] = entry;
            else _entries.Add(entry);
            Persist();
        }
        Changed?.Invoke();
    }

    public void Delete(string id)
    {
        lock (_sync)
        {
            _entries.RemoveAll(e => e.Id == id);
            Persist();
        }
        Changed?.Invoke();
    }

    public void SetEnabled(string id, bool enabled)
    {
        lock (_sync)
        {
            var e = _entries.FirstOrDefault(x => x.Id == id);
            if (e is null) return;
            e.Enabled = enabled;
            Persist();
        }
        Changed?.Invoke();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
                _entries = JsonSerializer.Deserialize<List<ScheduleEntry>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to load schedules.");
            _entries = [];
        }
    }

    private void Persist()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_entries, JsonOpts)); }
        catch (Exception e) { _logger.LogError(e, "Failed to save schedules."); }
    }
}
