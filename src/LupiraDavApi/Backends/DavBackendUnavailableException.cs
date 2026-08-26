namespace LupiraDavApi.Backends;

/// <summary>An upstream could not answer (unreachable, timeout, or 5xx) — surfaced to the DAV client as 503.</summary>
public sealed class DavBackendUnavailableException(string backend, string detail) : Exception($"Backend '{backend}' unavailable: {detail}")
{
    public string Backend { get; } = backend;
}
