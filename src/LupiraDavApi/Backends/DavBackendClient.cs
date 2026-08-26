using System.Net;
using System.Text;
using System.Text.Json;

namespace LupiraDavApi.Backends;

/// <summary>
/// HTTP implementation of one /dav-backend upstream. Service-authed: a cached client-credentials bearer
/// whose scope selects the backend's audience, or — when ServiceAuth is unconfigured (Development) —
/// the acting-user email as <c>X-Dev-User</c>. ETag preconditions and statuses pass through verbatim;
/// sync tokens are opaque strings.
/// </summary>
public sealed class DavBackendClient(string name, HttpClient http, string? scope, ServiceTokenProvider tokens) : IDavBackend
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => name;

    public async Task<DavCollectionsDto> CollectionsAsync(string email, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"dav-backend/u/{Seg(email)}/collections", email, null, ct);
        if (!resp.IsSuccessStatusCode) throw Unavailable(resp);
        return await ReadAsync<DavCollectionsDto>(resp, ct);
    }

    public async Task<DavResourcesDto?> QueryAsync(string email, Guid collectionId, DavQueryRequest request, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Post, $"dav-backend/u/{Seg(email)}/collections/{collectionId}/query", email,
            req => req.Content = JsonContent.Create(request, options: Json), ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode) throw Unavailable(resp);
        return await ReadAsync<DavResourcesDto>(resp, ct);
    }

    public async Task<DavBlob?> GetResourceAsync(string email, Guid collectionId, string uid, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, ResourceUrl(email, collectionId, uid), email, null, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode) throw Unavailable(resp);
        var content = await resp.Content.ReadAsStringAsync(ct);
        var contentType = resp.Content.Headers.ContentType?.MediaType ?? "text/calendar";
        var etag = resp.Headers.ETag?.Tag.Trim('"') ?? string.Empty;
        return new DavBlob(content, contentType, etag);
    }

    public async Task<DavWriteOutcome> PutResourceAsync(
        string email, Guid collectionId, string uid, string content, string contentType, string? ifMatch, bool ifNoneMatchStar, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Put, ResourceUrl(email, collectionId, uid), email, req =>
        {
            req.Content = new StringContent(content, Encoding.UTF8, contentType);
            if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
            if (ifNoneMatchStar) req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        }, ct);
        if ((int) resp.StatusCode >= 500) throw Unavailable(resp);
        return new DavWriteOutcome((int) resp.StatusCode, resp.Headers.ETag?.Tag.Trim('"'));
    }

    public async Task<int> DeleteResourceAsync(string email, Guid collectionId, string uid, string? ifMatch, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Delete, ResourceUrl(email, collectionId, uid), email, req =>
        {
            if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
        }, ct);
        if ((int) resp.StatusCode >= 500) throw Unavailable(resp);
        return (int) resp.StatusCode;
    }

    public async Task<DavChangesDto?> ChangesAsync(string email, Guid collectionId, string? since, CancellationToken ct)
    {
        var url = $"dav-backend/u/{Seg(email)}/collections/{collectionId}/changes"
                  + (since is null ? string.Empty : $"?since={Uri.EscapeDataString(since)}");
        using var resp = await SendAsync(HttpMethod.Get, url, email, null, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode) throw Unavailable(resp);
        return await ReadAsync<DavChangesDto>(resp, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string actingEmail, Action<HttpRequestMessage>? configure, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url);
        configure?.Invoke(req);
        if (tokens.IsConfigured && !string.IsNullOrWhiteSpace(scope))
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {await tokens.GetTokenAsync(scope!, ct)}");
        else
            req.Headers.TryAddWithoutValidation("X-Dev-User", actingEmail);   // Development-only backend auth

        try
        {
            return await http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new DavBackendUnavailableException(name, ex.Message);
        }
    }

    private static string ResourceUrl(string email, Guid collectionId, string uid) =>
        $"dav-backend/u/{Seg(email)}/collections/{collectionId}/resources/{Seg(uid)}";

    private static string Seg(string value) => Uri.EscapeDataString(value);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage resp, CancellationToken ct) =>
        await resp.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new DavBackendUnavailableException("?", "Empty response body.");

    private DavBackendUnavailableException Unavailable(HttpResponseMessage resp) =>
        new(name, $"HTTP {(int) resp.StatusCode}");
}
