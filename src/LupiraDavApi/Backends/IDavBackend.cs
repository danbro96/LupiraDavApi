namespace LupiraDavApi.Backends;

/// <summary>
/// One upstream /dav-backend (docs/dav-backend-contract.md). Reads return <c>null</c> for an upstream 404
/// (unknown OR inaccessible — opaque by contract); any transport failure or upstream 5xx throws
/// <see cref="DavBackendUnavailableException"/> so the router can fail the whole response (never a
/// partial home listing).
/// </summary>
public interface IDavBackend
{
    /// <summary>The backend's stable key: "cal", "tasks", or "contact".</summary>
    string Name { get; }

    Task<DavCollectionsDto> CollectionsAsync(string email, CancellationToken ct);
    Task<DavResourcesDto?> QueryAsync(string email, Guid collectionId, DavQueryRequest request, CancellationToken ct);
    Task<DavBlob?> GetResourceAsync(string email, Guid collectionId, string uid, CancellationToken ct);
    Task<DavWriteOutcome> PutResourceAsync(string email, Guid collectionId, string uid, string content, string contentType, string? ifMatch, bool ifNoneMatchStar, CancellationToken ct);
    Task<int> DeleteResourceAsync(string email, Guid collectionId, string uid, string? ifMatch, CancellationToken ct);
    Task<DavChangesDto?> ChangesAsync(string email, Guid collectionId, string? since, CancellationToken ct);
}

/// <summary>An upstream could not answer (unreachable, timeout, or 5xx) — surfaced to the DAV client as 503.</summary>
public sealed class DavBackendUnavailableException(string backend, string detail) : Exception($"Backend '{backend}' unavailable: {detail}")
{
    public string Backend { get; } = backend;
}
