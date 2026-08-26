using System.Net;
using Xunit;

namespace LupiraDavApi.IntegrationTests;

/// <summary>REPORT translation: sync-collection shuttles opaque tokens and renders tombstones as 404
/// responses; multiget forwards href uids; calendar-query forwards the time-range for server-side
/// (backend) expansion.</summary>
public sealed class SyncAndQueryTests : IntegrationTest
{
    private const string Ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:{0}\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    [Fact]
    public async Task Sync_collection_shuttles_tokens_and_renders_tombstones()
    {
        var calId = Factory.CalStub.AddCollection("Familj");
        Factory.CalStub.Seed(calId, "keep@x", string.Format(Ics, "keep@x"));
        Factory.CalStub.Seed(calId, "gone@x", string.Format(Ics, "gone@x"));

        var dav = Factory.DavClient(Email);
        var url = $"/dav/u/anna%40lupira.com/cal/ev-{calId}/";

        // Initial sync (no token): all live resources + a root sync-token.
        var initial = await ReadXml(await SendDav(dav, "REPORT", url, body: SyncCollectionBody(null)));
        Assert.Equal(2, initial.Descendants(D + "getetag").Count());
        var token = initial.Root!.Element(D + "sync-token")!.Value;
        Assert.False(string.IsNullOrEmpty(token));
        Assert.Equal("", Factory.CalStub.LastSince ?? "");   // empty token forwarded as null

        // Delete → incremental diff carries a 404 tombstone for the gone uid.
        await SendDav(dav, "DELETE", $"{url}gone%40x.ics");
        var diff = await ReadXml(await SendDav(dav, "REPORT", url, body: SyncCollectionBody(token)));
        Assert.Equal(token, Factory.CalStub.LastSince);      // opaque pass-through

        var tombstone = diff.Descendants(D + "response")
            .Single(r => r.Element(D + "href")!.Value.Contains("gone%40x"));
        Assert.Contains("404", tombstone.Element(D + "status")!.Value);
        Assert.DoesNotContain(diff.Descendants(D + "getetag"), _ => tombstone.Descendants(D + "getetag").Any());
    }

    [Fact]
    public async Task Multiget_forwards_uids_and_returns_calendar_data()
    {
        var calId = Factory.CalStub.AddCollection("Familj");
        Factory.CalStub.Seed(calId, "a@x", string.Format(Ics, "a@x"));
        Factory.CalStub.Seed(calId, "b@x", string.Format(Ics, "b@x"));

        var dav = Factory.DavClient(Email);
        var url = $"/dav/u/anna%40lupira.com/cal/ev-{calId}/";
        var doc = await ReadXml(await SendDav(dav, "REPORT", url,
            body: CalendarMultigetBody($"{url}a%40x.ics")));

        Assert.Equal(["a@x"], Factory.CalStub.LastQuery!.Uids);
        Assert.True(Factory.CalStub.LastQuery.IncludeContent);
        var data = doc.Descendants(C + "calendar-data").Single();
        Assert.Contains("UID:a@x", data.Value);
    }

    [Fact]
    public async Task Calendar_query_forwards_the_time_range()
    {
        var calId = Factory.CalStub.AddCollection("Familj");
        Factory.CalStub.Seed(calId, "a@x", string.Format(Ics, "a@x"));

        var dav = Factory.DavClient(Email);
        await SendDav(dav, "REPORT", $"/dav/u/anna%40lupira.com/cal/ev-{calId}/",
            body: TimeRangeQueryBody("20260701T000000Z", "20260801T000000Z"));

        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), Factory.CalStub.LastQuery!.Start);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), Factory.CalStub.LastQuery.End);
    }

    [Fact]
    public async Task Collection_depth1_propfind_lists_resource_etags()
    {
        var calId = Factory.CalStub.AddCollection("Familj");
        Factory.CalStub.Seed(calId, "a@x", string.Format(Ics, "a@x"));

        var dav = Factory.DavClient(Email);
        var doc = await ReadXml(await SendDav(dav, "PROPFIND", $"/dav/u/anna%40lupira.com/cal/ev-{calId}/", depth: "1"));

        var resource = doc.Descendants(D + "response").Single(r => r.Element(D + "href")!.Value.EndsWith(".ics"));
        Assert.Equal("\"1\"", resource.Descendants(D + "getetag").Single().Value);
        Assert.Null(Factory.CalStub.LastQuery!.Uids);
        Assert.False(Factory.CalStub.LastQuery.IncludeContent);   // listing never hauls blobs
    }

    [Fact]
    public async Task Unknown_collections_are_404_on_report()
    {
        var dav = Factory.DavClient(Email);
        var resp = await SendDav(dav, "REPORT", $"/dav/u/anna%40lupira.com/cal/ev-{Guid.NewGuid()}/",
            body: SyncCollectionBody(null));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
