using System.Net;
using Xunit;

namespace LupiraDavApi.IntegrationTests;

/// <summary>The gateway's own guarantees: the Basic challenge, the own-tree guard, forbidden WebDAV
/// verbs, and the hard 503-never-partial rule for home enumeration (a missing collection reads as a
/// deletion to DAVx5-class clients).</summary>
public sealed class AuthAndResilienceTests : IntegrationTest
{
    [Fact]
    public async Task Anonymous_requests_get_the_basic_challenge()
    {
        var anon = Factory.AnonymousClient();
        var resp = await SendDav(anon, "PROPFIND", "/dav/", depth: "0");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Contains("Basic realm=\"lupira-dav\"", resp.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task A_principal_cannot_address_another_principals_tree()
    {
        var dav = Factory.DavClient(Email);
        var resp = await SendDav(dav, "PROPFIND", "/dav/u/mallory%40lupira.com/cal/", depth: "1");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Theory]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    [InlineData("PROPPATCH")]
    [InlineData("MOVE")]
    [InlineData("LOCK")]
    public async Task Structural_webdav_verbs_are_forbidden(string method)
    {
        var dav = Factory.DavClient(Email);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendDav(dav, method, "/dav/u/anna%40lupira.com/cal/")).StatusCode);
    }

    [Fact]
    public async Task A_down_backend_fails_the_whole_home_listing_with_503()
    {
        Factory.CalStub.AddCollection("Familj");
        Factory.TasksStub.AddCollection("Groceries");
        Factory.TasksStub.Down = true;   // one of two calendar-home backends is unreachable

        var dav = Factory.DavClient(Email);
        var resp = await SendDav(dav, "PROPFIND", "/dav/u/anna%40lupira.com/cal/", depth: "1");

        // Never a partial home: a missing collection would be treated as deleted by the client.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task A_down_backend_fails_reads_and_writes_with_503()
    {
        var calId = Factory.CalStub.AddCollection("Familj");
        Factory.CalStub.Seed(calId, "a@x", "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n");
        Factory.CalStub.Down = true;

        var dav = Factory.DavClient(Email);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await dav.GetAsync($"/dav/u/anna%40lupira.com/cal/ev-{calId}/a%40x.ics")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await SendDav(dav, "REPORT", $"/dav/u/anna%40lupira.com/cal/ev-{calId}/", body: SyncCollectionBody(null))).StatusCode);
    }

    [Fact]
    public async Task Unknown_paths_are_404()
    {
        var dav = Factory.DavClient(Email);
        Assert.Equal(HttpStatusCode.NotFound, (await SendDav(dav, "PROPFIND", "/dav/junk/", depth: "0")).StatusCode);
    }
}
