using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LupiraDavApi.Backends;

/// <summary>
/// Mints and caches the gateway's Authentik client-credentials bearers, one per requested scope — the
/// scope's mapping is what injects the target backend's audience, so cal/tasks/contact each get their
/// own token. Generalized from lupira-cal-worker's proven provider (per-scope cache, 30 s expiry skew).
/// </summary>
public sealed class ServiceTokenProvider(IHttpClientFactory httpFactory, IOptions<ServiceAuthOptions> options)
{
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(30);

    private readonly ServiceAuthOptions _opts = options.Value;
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresAt)> _tokens = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public bool IsConfigured => _opts.IsConfigured;

    public async Task<string> GetTokenAsync(string scope, CancellationToken ct)
    {
        if (_tokens.TryGetValue(scope, out var cached) && DateTimeOffset.UtcNow < cached.ExpiresAt)
            return cached.Token;

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_tokens.TryGetValue(scope, out var fresh) && DateTimeOffset.UtcNow < fresh.ExpiresAt)
                return fresh.Token;

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _opts.ClientId!,
                ["client_secret"] = _opts.ClientSecret!,
                // Requesting the scope is what pulls in the audience mapping (e.g. aud=lupira-cal);
                // binding it on the provider alone is not enough — the backend rejects the token without it.
                ["scope"] = scope,
            };
            using var http = httpFactory.CreateClient(nameof(ServiceTokenProvider));
            using var resp = await http.PostAsync(_opts.TokenUrl, new FormUrlEncodedContent(form), ct);
            resp.EnsureSuccessStatusCode();
            var token = await resp.Content.ReadFromJsonAsync<TokenResponse>(ct)
                ?? throw new InvalidOperationException("Client-credentials token response was empty.");
            var access = token.AccessToken ?? throw new InvalidOperationException("Token response had no access_token.");
            _tokens[scope] = (access, DateTimeOffset.UtcNow + TimeSpan.FromSeconds(token.ExpiresIn ?? 300) - ExpirySkew);
            return access;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }
    }
}
