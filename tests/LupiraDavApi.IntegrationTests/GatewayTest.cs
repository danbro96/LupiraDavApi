using System.Text;
using System.Xml.Linq;
using Xunit;

namespace LupiraDavApi.IntegrationTests;

/// <summary>Base for gateway tests: a fresh factory + stubs per test, and DAV request/XML helpers.</summary>
public abstract class GatewayTest : IDisposable
{
    protected const string Email = "anna@lupira.com";

    protected readonly GatewayFactory Factory = new();

    protected static readonly XNamespace D = "DAV:";
    protected static readonly XNamespace C = "urn:ietf:params:xml:ns:caldav";
    protected static readonly XNamespace CR = "urn:ietf:params:xml:ns:carddav";
    protected static readonly XNamespace CS = "http://calendarserver.org/ns/";

    public void Dispose() => Factory.Dispose();

    protected static async Task<HttpResponseMessage> SendDav(
        HttpClient client, string method, string url, string? body = null, string? depth = null,
        string? ifMatch = null, string? ifNoneMatch = null, string contentType = "application/xml")
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), url);
        if (depth is not null) req.Headers.TryAddWithoutValidation("Depth", depth);
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (ifNoneMatch is not null) req.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        if (body is not null) req.Content = new StringContent(body, Encoding.UTF8, contentType);
        return await client.SendAsync(req);
    }

    protected static async Task<XDocument> ReadXml(HttpResponseMessage resp)
    {
        Assert.Equal(207, (int)resp.StatusCode);
        return XDocument.Parse(await resp.Content.ReadAsStringAsync());
    }

    protected static string SyncCollectionBody(string? token) =>
        $"""<?xml version="1.0" encoding="utf-8"?><d:sync-collection xmlns:d="DAV:"><d:sync-token>{token}</d:sync-token><d:sync-level>1</d:sync-level><d:prop><d:getetag/></d:prop></d:sync-collection>""";

    protected static string CalendarMultigetBody(params string[] hrefs)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="utf-8"?><c:calendar-multiget xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav"><d:prop><d:getetag/><c:calendar-data/></d:prop>""");
        foreach (var h in hrefs) sb.Append($"<d:href>{h}</d:href>");
        sb.Append("</c:calendar-multiget>");
        return sb.ToString();
    }

    protected static string TimeRangeQueryBody(string start, string end) =>
        $"""<?xml version="1.0" encoding="utf-8"?><c:calendar-query xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav"><d:prop><d:getetag/><c:calendar-data/></d:prop><c:filter><c:comp-filter name="VCALENDAR"><c:comp-filter name="VEVENT"><c:time-range start="{start}" end="{end}"/></c:comp-filter></c:comp-filter></c:filter></c:calendar-query>""";
}
