namespace LupiraDavApi.Backends;

/// <summary>One upstream /dav-backend: its in-network base URL and the Authentik scope whose mapping
/// injects that service's audience into the gateway's client-credentials token.</summary>
public sealed class BackendOptions
{
    public string BaseUrl { get; set; } = "";
    public string? Scope { get; set; }
}

/// <summary>Binds <c>Backends</c> — the three domain upstreams the gateway fans out to.</summary>
public sealed class BackendsOptions
{
    public const string SectionName = "Backends";

    public BackendOptions Cal { get; set; } = new();
    public BackendOptions Tasks { get; set; } = new();
    public BackendOptions Contact { get; set; } = new();
}

/// <summary>Binds <c>ServiceAuth</c> — the gateway's client-credentials identity (one confidential client,
/// per-backend audience-selecting scopes). Unset TokenUrl ⇒ dev mode: the acting-user email is sent as
/// <c>X-Dev-User</c> instead (backends accept it in Development only).</summary>
public sealed class ServiceAuthOptions
{
    public const string SectionName = "ServiceAuth";

    public string? TokenUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TokenUrl) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
