namespace MiniController.Web.Systems;

/// <summary>The set of systems the app knows about, built from DI.</summary>
public interface ISystemRegistry
{
    IReadOnlyList<ISystemModule> Modules { get; }
    ISystemModule? Find(string id);
}

public sealed class SystemRegistry(IEnumerable<ISystemModule> modules) : ISystemRegistry
{
    public IReadOnlyList<ISystemModule> Modules { get; } = modules.ToList();

    public ISystemModule? Find(string id) =>
        Modules.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
}
