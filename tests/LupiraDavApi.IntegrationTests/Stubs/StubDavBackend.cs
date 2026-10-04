using Lupira.Contracts.Dav;
using LupiraDavApi.Backends;
using Microsoft.AspNetCore.Http;

namespace LupiraDavApi.IntegrationTests.Stubs;

/// <summary>
/// An in-process, in-memory implementation of the /dav-backend contract (the house pattern for testing
/// proxies: stub the upstream, not the network). Honors ETag preconditions (etag = a version counter),
/// tracks a monotonic sync token with tombstones, and captures forwarded parameters so tests can assert
/// the gateway's pass-through behavior. <see cref="Down"/> simulates an unreachable upstream.
/// </summary>
public sealed class StubDavBackend(string name, DavCollectionKind kind) : IDavBackend
{
    private readonly Dictionary<Guid, string> _collections = [];
    private readonly Dictionary<Guid, Dictionary<string, (string Content, int Version)>> _resources = [];
    private readonly List<(long Seq, Guid CollectionId, string Uid, bool Deleted)> _log = [];
    private long _seq;

    public string Name => name;
    public bool Down { get; set; }

    // ---- captures for pass-through assertions ----
    public List<string> SeenEmails { get; } = [];
    public DavQueryRequest? LastQuery { get; private set; }
    public string? LastSince { get; private set; }
    public (string? IfMatch, bool IfNoneMatchStar)? LastPutPreconditions { get; private set; }

    public Guid AddCollection(string displayName)
    {
        var id = Guid.NewGuid();
        _collections[id] = displayName;
        _resources[id] = [];
        return id;
    }

    public void Seed(Guid collectionId, string uid, string content)
    {
        _resources[collectionId][uid] = (content, 1);
        _log.Add((++_seq, collectionId, uid, false));
    }

    private void EnsureUp()
    {
        if (Down) throw new DavBackendUnavailableException(name, "stubbed down");
    }

    public Task<DavCollectionsDto> CollectionsAsync(string email, CancellationToken ct)
    {
        EnsureUp();
        SeenEmails.Add(email);
        return Task.FromResult(new DavCollectionsDto
        {
            Principal = new DavPrincipalDto { DisplayName = email },
            Collections = [.. _collections.Select(kv => new DavCollectionDto
            {
                Id = kv.Key,
                Kind = kind,
                DisplayName = kv.Value,
                Ctag = $"seq-{_seq}",
                SyncToken = _seq.ToString(),
            })],
        });
    }

    public Task<DavResourcesDto?> QueryAsync(string email, Guid collectionId, DavQueryRequest request, CancellationToken ct)
    {
        EnsureUp();
        LastQuery = request;
        if (!_resources.TryGetValue(collectionId, out var items)) return Task.FromResult<DavResourcesDto?>(null);

        IEnumerable<KeyValuePair<string, (string Content, int Version)>> selected = items;
        if (request.Uids is { Count: > 0 } uids)
        {
            var set = uids.ToHashSet(StringComparer.Ordinal);
            selected = items.Where(kv => set.Contains(kv.Key));
        }
        return Task.FromResult<DavResourcesDto?>(new DavResourcesDto
        {
            Resources = [.. selected.Select(kv => new DavResourceDto
            {
                Uid = kv.Key,
                Etag = kv.Value.Version.ToString(),
                Content = request.IncludeContent ? kv.Value.Content : null,
            })],
        });
    }

    public Task<DavBlob?> GetResourceAsync(string email, Guid collectionId, string uid, CancellationToken ct)
    {
        EnsureUp();
        if (!_resources.TryGetValue(collectionId, out var items) || !items.TryGetValue(uid, out var r))
            return Task.FromResult<DavBlob?>(null);
        var contentType = kind == DavCollectionKind.AddressBook ? "text/vcard" : "text/calendar";
        return Task.FromResult<DavBlob?>(new DavBlob(r.Content, contentType, r.Version.ToString()));
    }

    public Task<DavWriteOutcome> PutResourceAsync(
        string email, Guid collectionId, string uid, string content, string contentType, string? ifMatch, bool ifNoneMatchStar, CancellationToken ct)
    {
        EnsureUp();
        LastPutPreconditions = (ifMatch, ifNoneMatchStar);
        if (!_resources.TryGetValue(collectionId, out var items))
            return Task.FromResult(new DavWriteOutcome(StatusCodes.Status404NotFound, null));

        var exists = items.TryGetValue(uid, out var existing);
        if (ifNoneMatchStar && exists) return Task.FromResult(new DavWriteOutcome(StatusCodes.Status412PreconditionFailed, null));
        if (ifMatch is not null && (!exists || existing.Version.ToString() != ifMatch))
            return Task.FromResult(new DavWriteOutcome(StatusCodes.Status412PreconditionFailed, null));

        var version = exists ? existing.Version + 1 : 1;
        items[uid] = (content, version);
        _log.Add((++_seq, collectionId, uid, false));
        return Task.FromResult(new DavWriteOutcome(
            exists ? StatusCodes.Status204NoContent : StatusCodes.Status201Created, version.ToString()));
    }

    public Task<int> DeleteResourceAsync(string email, Guid collectionId, string uid, string? ifMatch, CancellationToken ct)
    {
        EnsureUp();
        if (!_resources.TryGetValue(collectionId, out var items) || !items.TryGetValue(uid, out var existing))
            return Task.FromResult(StatusCodes.Status404NotFound);
        if (ifMatch is not null && existing.Version.ToString() != ifMatch)
            return Task.FromResult(StatusCodes.Status412PreconditionFailed);

        items.Remove(uid);
        _log.Add((++_seq, collectionId, uid, true));
        return Task.FromResult(StatusCodes.Status204NoContent);
    }

    public Task<DavChangesDto?> ChangesAsync(string email, Guid collectionId, string? since, CancellationToken ct)
    {
        EnsureUp();
        LastSince = since;
        if (!_resources.TryGetValue(collectionId, out var items)) return Task.FromResult<DavChangesDto?>(null);

        if (!long.TryParse(since, out var token))   // absent/garbage → full live listing
            return Task.FromResult<DavChangesDto?>(new DavChangesDto
            {
                SyncToken = _seq.ToString(),
                Changed = [.. items.Select(kv => new DavChangeDto { Uid = kv.Key, Etag = kv.Value.Version.ToString() })],
                Deleted = [],
            });

        var relevant = _log.Where(e => e.Seq > token && e.CollectionId == collectionId).ToList();
        var deleted = relevant.Where(e => e.Deleted).Select(e => e.Uid).Distinct().Where(u => !items.ContainsKey(u)).ToList();
        var changed = relevant.Where(e => !e.Deleted).Select(e => e.Uid).Distinct().Where(items.ContainsKey).ToList();
        return Task.FromResult<DavChangesDto?>(new DavChangesDto
        {
            SyncToken = _seq.ToString(),
            Changed = [.. changed.Select(u => new DavChangeDto { Uid = u, Etag = items[u].Version.ToString() })],
            Deleted = deleted,
        });
    }
}
