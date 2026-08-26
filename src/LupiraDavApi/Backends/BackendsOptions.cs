namespace LupiraDavApi.Backends;

/// <summary>Binds <c>Backends</c> — the three domain upstreams the gateway fans out to.</summary>
public sealed class BackendsOptions
{
    public const string SectionName = "Backends";

    public BackendOptions Cal { get; set; } = new();
    public BackendOptions Tasks { get; set; } = new();
    public BackendOptions Contact { get; set; } = new();
}
