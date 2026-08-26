namespace LupiraDavApi.Backends;

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
