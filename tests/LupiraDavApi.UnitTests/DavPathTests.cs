using LupiraDavApi.Dav;
using Xunit;

namespace LupiraDavApi.UnitTests;

/// <summary>The unified layout parse/build round-trip: ev-/td- markers pick the backend statelessly,
/// emails escape/unescape symmetrically, and malformed paths degrade to Unknown.</summary>
public sealed class DavPathTests
{
    private static readonly Guid Id = Guid.Parse("3f9c0000-0000-0000-0000-00000000abcd");

    [Fact]
    public void Root_and_principal_parse()
    {
        Assert.Equal(DavTargetKind.Root, DavPath.Parse("/dav/").Kind);
        var p = DavPath.Parse("/dav/u/anna%40lupira.com/");
        Assert.Equal(DavTargetKind.Principal, p.Kind);
        Assert.Equal("anna@lupira.com", p.Email);
    }

    [Fact]
    public void Homes_parse()
    {
        Assert.Equal(DavTargetKind.CalendarHome, DavPath.Parse("/dav/u/a%40x.se/cal/").Kind);
        Assert.Equal(DavTargetKind.AddressBookHome, DavPath.Parse("/dav/u/a%40x.se/card/").Kind);
    }

    [Theory]
    [InlineData("ev-", "cal")]
    [InlineData("td-", "tasks")]
    public void Calendar_home_markers_pick_the_backend(string marker, string backend)
    {
        var col = DavPath.Parse($"/dav/u/a%40x.se/cal/{marker}{Id}/");
        Assert.Equal(DavTargetKind.Collection, col.Kind);
        Assert.Equal(backend, col.Backend);
        Assert.Equal(Id, col.CollectionId);

        var res = DavPath.Parse($"/dav/u/a%40x.se/cal/{marker}{Id}/evt-1%40x.ics");
        Assert.Equal(DavTargetKind.Resource, res.Kind);
        Assert.Equal(backend, res.Backend);
        Assert.Equal("evt-1@x", res.Uid);
    }

    [Fact]
    public void Card_collections_belong_to_the_contact_backend()
    {
        var res = DavPath.Parse($"/dav/u/a%40x.se/card/{Id}/card-1%40x.vcf");
        Assert.Equal(DavTargetKind.Resource, res.Kind);
        Assert.Equal("contact", res.Backend);
        Assert.Equal(Id, res.CollectionId);
        Assert.Equal("card-1@x", res.Uid);
    }

    [Fact]
    public void Unmarked_or_malformed_calendar_segments_are_unknown()
    {
        Assert.Equal(DavTargetKind.Unknown, DavPath.Parse($"/dav/u/a%40x.se/cal/{Id}/").Kind);            // no marker
        Assert.Equal(DavTargetKind.Unknown, DavPath.Parse("/dav/u/a%40x.se/cal/ev-not-a-guid/").Kind);
        Assert.Equal(DavTargetKind.Unknown, DavPath.Parse("/dav/u/a%40x.se/junk/").Kind);
        Assert.Equal(DavTargetKind.Unknown, DavPath.Parse("/other/").Kind);
        Assert.Equal(DavTargetKind.Unknown, DavPath.Parse($"/dav/u/a%40x.se/cal/ev-{Id}/x/y").Kind);      // too deep
    }

    [Theory]
    [InlineData("anna+test@lupira.com")]
    [InlineData("åsa@lupira.com")]
    public void Email_escaping_round_trips(string email)
    {
        var href = DavPath.CollectionHref("https://dav-api.lupira.com", email, "cal", Id);
        var relative = href["https://dav-api.lupira.com".Length..];
        var parsed = DavPath.Parse(relative);
        Assert.Equal(email, parsed.Email);
        Assert.Equal(Id, parsed.CollectionId);
    }

    [Fact]
    public void Resource_hrefs_round_trip_including_uid_extension()
    {
        var href = DavPath.ResourceHref("", "a@x.se", "tasks", Id, "todo-1@x");
        Assert.EndsWith(".ics", href);
        var parsed = DavPath.Parse(href);
        Assert.Equal("todo-1@x", parsed.Uid);
        Assert.Equal("tasks", parsed.Backend);

        var card = DavPath.ResourceHref("", "a@x.se", "contact", Id, "card-1@x");
        Assert.EndsWith(".vcf", card);
        Assert.Equal("card-1@x", DavPath.Parse(card).Uid);
    }

    [Fact]
    public void Emails_are_lowercased_on_parse()
    {
        Assert.Equal("anna@lupira.com", DavPath.Parse("/dav/u/Anna%40Lupira.COM/").Email);
    }
}
