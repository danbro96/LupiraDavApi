using System.Net;
using Xunit;

namespace LupiraDavApi.IntegrationTests;

/// <summary>Blob pass-through: PUT/GET/DELETE relay bodies, ETags, preconditions, and statuses verbatim
/// between the DAV client and the owning backend. The gateway never retries a write.</summary>
public sealed class ReadWriteTests : GatewayTest
{
    private const string Ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:evt-1@x\r\nSUMMARY:Standup\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    [Fact]
    public async Task Put_creates_and_relays_the_backend_etag()
    {
        var calId = Factory.CalStub.AddCollection("Familj");
        var dav = Factory.DavClient(Email);
        var url = $"/dav/u/anna%40lupira.com/cal/ev-{calId}/evt-1%40x.ics";

        var put = await SendDav(dav, "PUT", url, body: Ics, contentType: "text/calendar");
        Assert.Equal(HttpStatusCode.Created, put.StatusCode);
        Assert.Equal("\"1\"", put.Headers.ETag!.Tag);

        var get = await dav.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("\"1\"", get.Headers.ETag!.Tag);
        Assert.StartsWith("text/calendar", get.Content.Headers.ContentType!.ToString());
        Assert.Equal(Ics, await get.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Preconditions_pass_through_and_412s_relay()
    {
        var calId = Factory.CalStub.AddCollection("Familj");
        var dav = Factory.DavClient(Email);
        var url = $"/dav/u/anna%40lupira.com/cal/ev-{calId}/evt-1%40x.ics";
        await SendDav(dav, "PUT", url, body: Ics, contentType: "text/calendar");

        var dupCreate = await SendDav(dav, "PUT", url, body: Ics, contentType: "text/calendar", ifNoneMatch: "*");
        Assert.Equal(HttpStatusCode.PreconditionFailed, dupCreate.StatusCode);
        Assert.Equal((null, true), Factory.CalStub.LastPutPreconditions);

        var stale = await SendDav(dav, "PUT", url, body: Ics, contentType: "text/calendar", ifMatch: "\"999\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(("999", false), Factory.CalStub.LastPutPreconditions);

        var ok = await SendDav(dav, "PUT", url, body: Ics, contentType: "text/calendar", ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal("\"2\"", ok.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Delete_relays_status_and_benign_retry_is_404()
    {
        var calId = Factory.CalStub.AddCollection("Familj");
        var dav = Factory.DavClient(Email);
        var url = $"/dav/u/anna%40lupira.com/cal/ev-{calId}/evt-1%40x.ics";
        await SendDav(dav, "PUT", url, body: Ics, contentType: "text/calendar");

        Assert.Equal(HttpStatusCode.PreconditionFailed, (await SendDav(dav, "DELETE", url, ifMatch: "\"999\"")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendDav(dav, "DELETE", url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendDav(dav, "DELETE", url)).StatusCode);
    }

    [Fact]
    public async Task Vcf_resources_route_to_the_contact_backend()
    {
        var bookId = Factory.ContactStub.AddCollection("Personal");
        var dav = Factory.DavClient(Email);
        var vcf = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:card-1@x\r\nFN:Jane\r\nEND:VCARD\r\n";
        var url = $"/dav/u/anna%40lupira.com/card/{bookId}/card-1%40x.vcf";

        Assert.Equal(HttpStatusCode.Created, (await SendDav(dav, "PUT", url, body: vcf, contentType: "text/vcard")).StatusCode);
        var get = await dav.GetAsync(url);
        Assert.StartsWith("text/vcard", get.Content.Headers.ContentType!.ToString());
        Assert.Equal(vcf, await get.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_resources_are_404()
    {
        var calId = Factory.CalStub.AddCollection("Familj");
        var dav = Factory.DavClient(Email);
        Assert.Equal(HttpStatusCode.NotFound,
            (await dav.GetAsync($"/dav/u/anna%40lupira.com/cal/ev-{calId}/nope.ics")).StatusCode);
    }
}
