namespace LupiraDavApi.Backends;

/// <summary>Outcome of a PUT at the seam: the HTTP status to relay and, on success, the new unquoted ETag.</summary>
public sealed record DavWriteOutcome(int Status, string? Etag);
