namespace LupiraDavApi.Backends;

/// <summary>A fetched resource blob: raw content, its media type, and the unquoted ETag.</summary>
public sealed record DavBlob(string Content, string ContentType, string Etag);
