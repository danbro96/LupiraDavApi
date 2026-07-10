using LupiraDavApi.Dav;
using Xunit;

namespace LupiraDavApi.UnitTests;

/// <summary>Pure protocol parsing, ported from LupiraCalApi's retired router: hostile XML, opaque sync
/// tokens, multiget href extraction, time-range attributes, and ETag preconditions.</summary>
public sealed class DavProtocolTests
{
    [Fact]
    public void TryParseXml_rejects_junk_and_accepts_xml()
    {
        Assert.Null(DavProtocol.TryParseXml(""));
        Assert.Null(DavProtocol.TryParseXml("not xml <"));
        Assert.NotNull(DavProtocol.TryParseXml("""<d:sync-collection xmlns:d="DAV:"/>"""));
    }

    [Theory]
    [InlineData("4812", "4812")]
    [InlineData("opaque-token", "opaque-token")]   // tokens are NOT parsed as numbers here
    [InlineData("", null)]
    public void Sync_token_is_opaque(string value, string? expected)
    {
        var doc = DavProtocol.TryParseXml(
            $"""<d:sync-collection xmlns:d="DAV:"><d:sync-token>{value}</d:sync-token></d:sync-collection>""")!;
        Assert.Equal(expected, DavProtocol.ParseSyncToken(doc));
    }

    [Fact]
    public void Multiget_hrefs_extract_unescaped_uids()
    {
        var body = """
            <c:calendar-multiget xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
              <d:href>/dav/u/a%40x.se/cal/ev-00000000-0000-0000-0000-000000000001/evt-1%40x.ics</d:href>
              <d:href>/dav/u/a%40x.se/cal/ev-00000000-0000-0000-0000-000000000001/evt-2%40x.ICS</d:href>
            </c:calendar-multiget>
            """;
        var uids = DavProtocol.ExtractHrefUids(body, ".ics");
        Assert.Equal(2, uids.Count);
        Assert.Contains("evt-1@x", uids);
        Assert.Contains("evt-2@x", uids);
    }

    [Fact]
    public void Time_range_parses_utc_attributes()
    {
        var doc = DavProtocol.TryParseXml("""
            <c:calendar-query xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
              <c:filter><c:comp-filter name="VCALENDAR"><c:comp-filter name="VEVENT">
                <c:time-range start="20260701T000000Z" end="20260801T000000Z"/>
              </c:comp-filter></c:comp-filter></c:filter>
            </c:calendar-query>
            """);
        var range = DavProtocol.ParseTimeRange(doc);
        Assert.NotNull(range);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), range!.Value.Start);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), range.Value.End);
    }

    [Theory]
    [InlineData(null, null, null, false)]
    [InlineData("\"abc\"", null, "abc", false)]
    [InlineData("*", null, null, false)]
    [InlineData(null, "*", null, true)]
    [InlineData("\"abc\"", "*", "abc", true)]
    public void Preconditions_parse(string? ifMatch, string? ifNoneMatch, string? expectedTag, bool expectedStar)
    {
        var (tag, star) = DavProtocol.ParsePreconditions(ifMatch, ifNoneMatch);
        Assert.Equal(expectedTag, tag);
        Assert.Equal(expectedStar, star);
    }
}
