namespace LupiraDavApi.Dav;

/// <summary>Where in the unified tree a request points.</summary>
internal enum DavTargetKind { Root, Principal, CalendarHome, AddressBookHome, Collection, Resource, Unknown }

/// <summary>A parsed /dav path: the principal email (unescaped, lowercased), and — under a home — the
/// owning backend ("cal" | "tasks" | "contact"), collection id, and resource uid.</summary>
internal sealed record DavTarget(
    DavTargetKind Kind,
    string? Email = null,
    string? Backend = null,
    Guid CollectionId = default,
    string? Uid = null,
    bool IsCalendarHome = false);

/// <summary>
/// The unified URL layout — pure parse/build, no I/O. The collection-segment markers make routing
/// stateless: the gateway knows the owning backend from the path alone.
///
///   /dav/                                    root (current-user-principal)
///   /dav/u/{email}/                          principal
///   /dav/u/{email}/cal/                      calendar-home-set (VEVENT calendars + VTODO lists)
///   /dav/u/{email}/cal/ev-{calendarId}/      an event calendar   → cal backend
///   /dav/u/{email}/cal/td-{listId}/          a task list         → tasks backend
///   /dav/u/{email}/cal/ev-{id}/{uid}.ics     an event            → cal backend
///   /dav/u/{email}/card/                     addressbook-home-set
///   /dav/u/{email}/card/{addressBookId}/     an address book     → contact backend
///   /dav/u/{email}/card/{id}/{uid}.vcf       a contact           → contact backend
/// </summary>
internal static class DavPath
{
    public const string CalMarker = "ev-";
    public const string TasksMarker = "td-";

    public static DavTarget Parse(string path)
    {
        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments[0] != "dav") return new DavTarget(DavTargetKind.Unknown);
        var rest = segments[1..];

        if (rest.Length == 0) return new DavTarget(DavTargetKind.Root);
        if (rest[0] != "u" || rest.Length < 2) return new DavTarget(DavTargetKind.Unknown);

        var email = Uri.UnescapeDataString(rest[1]).Trim().ToLowerInvariant();
        if (email.Length == 0) return new DavTarget(DavTargetKind.Unknown);
        if (rest.Length == 2) return new DavTarget(DavTargetKind.Principal, email);

        return rest[2] switch
        {
            "cal" => ParseHome(email, rest[3..], isCalendar: true),
            "card" => ParseHome(email, rest[3..], isCalendar: false),
            _ => new DavTarget(DavTargetKind.Unknown, email),
        };
    }

    private static DavTarget ParseHome(string email, string[] rest, bool isCalendar)
    {
        if (rest.Length == 0)
            return new DavTarget(isCalendar ? DavTargetKind.CalendarHome : DavTargetKind.AddressBookHome, email, IsCalendarHome: isCalendar);

        var (backend, rawId) = isCalendar switch
        {
            true when rest[0].StartsWith(CalMarker, StringComparison.Ordinal) => ("cal", rest[0][CalMarker.Length..]),
            true when rest[0].StartsWith(TasksMarker, StringComparison.Ordinal) => ("tasks", rest[0][TasksMarker.Length..]),
            true => ((string?) null, rest[0]),
            false => ("contact", rest[0]),
        };
        if (backend is null || !Guid.TryParse(rawId, out var collectionId))
            return new DavTarget(DavTargetKind.Unknown, email);

        if (rest.Length == 1)
            return new DavTarget(DavTargetKind.Collection, email, backend, collectionId, IsCalendarHome: isCalendar);
        if (rest.Length == 2)
            return new DavTarget(DavTargetKind.Resource, email, backend, collectionId,
                Uid: Uri.UnescapeDataString(StripExt(rest[1])), IsCalendarHome: isCalendar);

        return new DavTarget(DavTargetKind.Unknown, email);
    }

    // ---- href builders (symmetric with Parse; emails and uids are escaped) ----

    public static string PrincipalHref(string baseUrl, string email) => $"{baseUrl}/dav/u/{Seg(email)}/";
    public static string CalendarHomeHref(string baseUrl, string email) => $"{baseUrl}/dav/u/{Seg(email)}/cal/";
    public static string AddressBookHomeHref(string baseUrl, string email) => $"{baseUrl}/dav/u/{Seg(email)}/card/";

    public static string CollectionHref(string baseUrl, string email, string backend, Guid collectionId) => backend switch
    {
        "cal" => $"{baseUrl}/dav/u/{Seg(email)}/cal/{CalMarker}{collectionId}/",
        "tasks" => $"{baseUrl}/dav/u/{Seg(email)}/cal/{TasksMarker}{collectionId}/",
        _ => $"{baseUrl}/dav/u/{Seg(email)}/card/{collectionId}/",
    };

    public static string ResourceHref(string baseUrl, string email, string backend, Guid collectionId, string uid) =>
        CollectionHref(baseUrl, email, backend, collectionId) + Seg(uid) + (backend == "contact" ? ".vcf" : ".ics");

    internal static string StripExt(string file)
    {
        var dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }

    private static string Seg(string value) => Uri.EscapeDataString(value);
}
