namespace LupiraDavApi.Dav;

/// <summary>A parsed /dav path: the principal email (unescaped, lowercased), and — under a home — the
/// owning backend ("cal" | "tasks" | "contact"), collection id, and resource uid.</summary>
internal sealed record DavTarget(
    DavTargetKind Kind,
    string? Email = null,
    string? Backend = null,
    Guid CollectionId = default,
    string? Uid = null,
    bool IsCalendarHome = false);
