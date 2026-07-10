using System.Text.Json.Serialization;

namespace LupiraDavApi.Backends;

// The wire shapes of the internal /dav-backend contract (docs/dav-backend-contract.md), implemented
// identically by lupira-cal-api, lupira-tasks-api, and lupira-contact-api. ETags are unquoted in JSON
// bodies; sync tokens are opaque strings this gateway never parses.

[JsonConverter(typeof(JsonStringEnumConverter<DavCollectionKind>))]
public enum DavCollectionKind { EventCalendar, TodoList, AddressBook }

public sealed class DavPrincipalDto
{
    public string? DisplayName { get; set; }
}

public sealed class DavCollectionDto
{
    public required Guid Id { get; set; }
    public required DavCollectionKind Kind { get; set; }
    public string? DisplayName { get; set; }
    public required string Ctag { get; set; }
    public required string SyncToken { get; set; }
}

public sealed class DavCollectionsDto
{
    public required DavPrincipalDto Principal { get; set; }
    public required List<DavCollectionDto> Collections { get; set; }
}

public sealed class DavQueryRequest
{
    public List<string>? Uids { get; set; }
    public DateTimeOffset? Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public bool IncludeContent { get; set; }
}

public sealed class DavResourceDto
{
    public required string Uid { get; set; }
    public required string Etag { get; set; }
    public string? Content { get; set; }
}

public sealed class DavResourcesDto
{
    public required List<DavResourceDto> Resources { get; set; }
}

public sealed class DavChangeDto
{
    public required string Uid { get; set; }
    public required string Etag { get; set; }
}

public sealed class DavChangesDto
{
    public required string SyncToken { get; set; }
    public required List<DavChangeDto> Changed { get; set; }
    public required List<string> Deleted { get; set; }
}

/// <summary>A fetched resource blob: raw content, its media type, and the unquoted ETag.</summary>
public sealed record DavBlob(string Content, string ContentType, string Etag);

/// <summary>Outcome of a PUT at the seam: the HTTP status to relay and, on success, the new unquoted ETag.</summary>
public sealed record DavWriteOutcome(int Status, string? Etag);
