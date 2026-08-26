namespace LupiraDavApi.Backends;

/// <summary>One upstream /dav-backend: its in-network base URL and the Authentik scope whose mapping
/// injects that service's audience into the gateway's client-credentials token.</summary>
public sealed class BackendOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public string? Scope { get; set; }
}
