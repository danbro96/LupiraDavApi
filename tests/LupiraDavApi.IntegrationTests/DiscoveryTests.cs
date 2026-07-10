using System.Net;
using Xunit;

namespace LupiraDavApi.IntegrationTests;

/// <summary>The discovery chain a fresh DAVx5/iOS account walks: well-knowns → root → principal → the
/// unified homes. One calendar-home-set serves both VEVENT calendars and VTODO lists, distinguished by
/// supported-calendar-component-set.</summary>
public sealed class DiscoveryTests : GatewayTest
{
    [Fact]
    public async Task Well_knowns_redirect_anonymously_to_dav()
    {
        var anon = Factory.AnonymousClient();
        foreach (var path in new[] { "/.well-known/caldav", "/.well-known/carddav" })
        {
            var resp = await anon.GetAsync(path);
            Assert.Equal(HttpStatusCode.MovedPermanently, resp.StatusCode);
            Assert.Equal("/dav/", resp.Headers.Location?.ToString());
        }
    }

    [Fact]
    public async Task Options_advertises_caldav_and_carddav()
    {
        var dav = Factory.DavClient(Email);
        var resp = await SendDav(dav, "OPTIONS", "/dav/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("calendar-access", resp.Headers.GetValues("DAV").Single());
        Assert.Contains("addressbook", resp.Headers.GetValues("DAV").Single());
    }

    [Fact]
    public async Task Root_names_the_current_user_principal()
    {
        var dav = Factory.DavClient(Email);
        var doc = await ReadXml(await SendDav(dav, "PROPFIND", "/dav/", depth: "0"));
        var principal = doc.Descendants(D + "current-user-principal").Single().Element(D + "href")!.Value;
        Assert.Contains("/dav/u/anna%40lupira.com/", principal);
    }

    [Fact]
    public async Task Principal_advertises_both_home_sets()
    {
        var dav = Factory.DavClient(Email);
        var doc = await ReadXml(await SendDav(dav, "PROPFIND", "/dav/u/anna%40lupira.com/", depth: "0"));
        Assert.Contains("/cal/", doc.Descendants(C + "calendar-home-set").Single().Value);
        Assert.Contains("/card/", doc.Descendants(CR + "addressbook-home-set").Single().Value);
    }

    [Fact]
    public async Task Calendar_home_lists_both_kinds_with_their_component_sets()
    {
        var calId = Factory.CalStub.AddCollection("Familj");
        var listId = Factory.TasksStub.AddCollection("Groceries");

        var dav = Factory.DavClient(Email);
        var doc = await ReadXml(await SendDav(dav, "PROPFIND", "/dav/u/anna%40lupira.com/cal/", depth: "1"));

        var responses = doc.Descendants(D + "response").ToList();
        Assert.Equal(3, responses.Count);   // home + one of each kind

        var ev = responses.Single(r => r.Element(D + "href")!.Value.Contains($"ev-{calId}"));
        Assert.Equal("VEVENT", ev.Descendants(C + "comp").Single().Attribute("name")!.Value);
        Assert.Contains("Familj", ev.Descendants(D + "displayname").Single().Value);

        var td = responses.Single(r => r.Element(D + "href")!.Value.Contains($"td-{listId}"));
        Assert.Equal("VTODO", td.Descendants(C + "comp").Single().Attribute("name")!.Value);
    }

    [Fact]
    public async Task Addressbook_home_lists_contact_collections()
    {
        var bookId = Factory.ContactStub.AddCollection("Personal");
        var dav = Factory.DavClient(Email);
        var doc = await ReadXml(await SendDav(dav, "PROPFIND", "/dav/u/anna%40lupira.com/card/", depth: "1"));

        var book = doc.Descendants(D + "response")
            .Single(r => r.Element(D + "href")!.Value.Contains(bookId.ToString()));
        Assert.NotNull(book.Descendants(CR + "addressbook").SingleOrDefault());
        Assert.StartsWith("\"seq-", book.Descendants(CS + "getctag").Single().Value);
    }

    [Fact]
    public async Task Home_listing_provisions_the_principal_at_each_backend()
    {
        var dav = Factory.DavClient(Email);
        await SendDav(dav, "PROPFIND", "/dav/u/anna%40lupira.com/cal/", depth: "1");
        Assert.Contains(Email, Factory.CalStub.SeenEmails);
        Assert.Contains(Email, Factory.TasksStub.SeenEmails);
    }
}
