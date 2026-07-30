using System.Globalization;
using System.Xml.Linq;

namespace LupiraDavApi.Dav;

/// <summary>
/// The pure (no HttpContext, no upstream) protocol logic behind <see cref="DavRouter"/>: request-body
/// parsing and ETag-precondition parsing. Adapted from LupiraCalApi's retired in-process router; the
/// time-range overlap math moved into the backends (server-side expansion), and the sync token is an
/// opaque string here — only the owning backend parses it.
/// </summary>
internal static class DavProtocol
{
    static readonly XNamespace D = "DAV:";

    public static XDocument? TryParseXml(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try { return XDocument.Parse(body); } catch { return null; }
    }

    /// <summary>The sync-token element's value, passed to the owning backend verbatim (empty → null = initial sync).</summary>
    public static string? ParseSyncToken(XDocument doc)
    {
        var v = doc.Descendants(D + "sync-token").FirstOrDefault()?.Value.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    public static (DateTimeOffset Start, DateTimeOffset End)? ParseTimeRange(XDocument? doc)
    {
        var tr = doc?.Descendants().FirstOrDefault(x => x.Name.LocalName == "time-range");
        if (tr is null) return null;
        var s = ParseICalUtc(tr.Attribute("start")?.Value);
        var e = ParseICalUtc(tr.Attribute("end")?.Value);
        return s is { } start && e is { } end ? (start, end) : null;
    }

    public static DateTimeOffset? ParseICalUtc(string? s) =>
        !string.IsNullOrEmpty(s) && DateTimeOffset.TryParseExact(
            s, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;

    public static List<string> ExtractHrefUids(string body, string ext)
    {
        var uids = new HashSet<string>();
        if (string.IsNullOrWhiteSpace(body)) return [];
        try
        {
            var doc = XDocument.Parse(body);
            foreach (var href in doc.Descendants(D + "href"))
            {
                var name = href.Value.TrimEnd('/');
                var slash = name.LastIndexOf('/');
                if (slash >= 0) name = name[(slash + 1)..];
                if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) name = name[..^ext.Length];
                if (name.Length > 0) uids.Add(Uri.UnescapeDataString(name));
            }
        }
        catch { /* malformed → treat as query (return all) */ }

        return [.. uids];
    }

    /// <summary>Parses the raw <c>If-Match</c> / <c>If-None-Match</c> header values. An <c>If-Match</c> of <c>*</c>
    /// (or empty) is treated as "no specific tag"; quotes are stripped from a concrete tag. Returns whether
    /// <c>If-None-Match</c> is the <c>*</c> wildcard (the "create only if absent" guard).</summary>
    public static (string? IfMatch, bool IfNoneMatchStar) ParsePreconditions(string? ifMatchHeader, string? ifNoneMatchHeader)
    {
        string? ifMatch = null;
        var im = ifMatchHeader?.Trim();
        if (!string.IsNullOrEmpty(im) && im != "*") ifMatch = im.Trim('"');
        var inm = ifNoneMatchHeader?.Trim() ?? "";
        return (ifMatch, inm == "*");
    }
}
