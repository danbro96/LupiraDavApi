using System.Xml.Linq;
using LupiraDavApi.Backends;

namespace LupiraDavApi.Dav;

/// <summary>Multistatus/propstat builders for the unified DAV tree — extracted from LupiraCalApi's retired
/// in-process router. Pure XML assembly; hrefs come from <see cref="DavPath"/>.</summary>
internal static class DavXml
{
    public static readonly XNamespace D = "DAV:";
    public static readonly XNamespace C = "urn:ietf:params:xml:ns:caldav";
    public static readonly XNamespace CR = "urn:ietf:params:xml:ns:carddav";
    public static readonly XNamespace CS = "http://calendarserver.org/ns/";

    public static XElement MultiStatus(params XElement[] responses) => new(
        D + "multistatus",
        new XAttribute(XNamespace.Xmlns + "d", D.NamespaceName),
        new XAttribute(XNamespace.Xmlns + "c", C.NamespaceName),
        new XAttribute(XNamespace.Xmlns + "cr", CR.NamespaceName),
        new XAttribute(XNamespace.Xmlns + "cs", CS.NamespaceName),
        responses);

    public static XElement MultiStatusWithToken(string token, XElement[] responses)
    {
        var ms = MultiStatus(responses);
        ms.Add(new XElement(D + "sync-token", token));
        return ms;
    }

    public static XElement Response(string href, params XElement[] props) => new(
        D + "response",
        new XElement(D + "href", href),
        new XElement(
            D + "propstat",
            new XElement(D + "prop", props.Cast<object>().ToArray()),
            new XElement(D + "status", "HTTP/1.1 200 OK")));

    /// <summary>A deleted resource in a sync REPORT (RFC 6578 tombstone).</summary>
    public static XElement DeletedResponse(string href) => new(
        D + "response",
        new XElement(D + "href", href),
        new XElement(D + "status", "HTTP/1.1 404 Not Found"));

    public static XElement Href(string url) => new(D + "href", url);

    public static string Etag(string unquoted) => $"\"{unquoted}\"";

    public static XElement SupportedReports(params XName[] reports) => new(
        D + "supported-report-set",
        reports.Select(r => new XElement(D + "supported-report", new XElement(D + "report", new XElement(r)))));

    /// <summary>Collection props for a calendar-home member: an event calendar advertises VEVENT, a task
    /// list VTODO — the one <c>calendar-home-set</c> serves both, distinguished by component set.</summary>
    public static XElement[] CalendarProps(DavCollectionDto c) =>
    [
        new XElement(D + "resourcetype", new XElement(D + "collection"), new XElement(C + "calendar")),
        new XElement(D + "displayname", c.DisplayName ?? c.Id.ToString()),
        new XElement(CS + "getctag", Etag(c.Ctag)),
        new XElement(D + "sync-token", c.SyncToken),
        new XElement(
            C + "supported-calendar-component-set",
            new XElement(C + "comp", new XAttribute("name", c.Kind == DavCollectionKind.TodoList ? "VTODO" : "VEVENT"))),
        SupportedReports(C + "calendar-query", C + "calendar-multiget", D + "sync-collection"),
    ];

    public static XElement[] AddressbookProps(DavCollectionDto a) =>
    [
        new XElement(D + "resourcetype", new XElement(D + "collection"), new XElement(CR + "addressbook")),
        new XElement(D + "displayname", a.DisplayName ?? a.Id.ToString()),
        new XElement(CS + "getctag", Etag(a.Ctag)),
        new XElement(D + "sync-token", a.SyncToken),
        SupportedReports(CR + "addressbook-query", CR + "addressbook-multiget", D + "sync-collection"),
    ];
}
