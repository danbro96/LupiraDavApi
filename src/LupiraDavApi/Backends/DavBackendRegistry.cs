namespace LupiraDavApi.Backends;

/// <summary>The three upstreams, resolvable by name ("cal" | "tasks" | "contact") — the path marker in
/// the unified DAV layout picks the owning backend statelessly (DavPath).</summary>
public sealed class DavBackendRegistry(IEnumerable<IDavBackend> backends)
{
    private readonly Dictionary<string, IDavBackend> _byName =
        backends.ToDictionary(b => b.Name, StringComparer.Ordinal);

    public IDavBackend Cal => _byName["cal"];

    public IDavBackend Tasks => _byName["tasks"];

    public IDavBackend Contact => _byName["contact"];

    public IDavBackend Get(string name) => _byName[name];
}
