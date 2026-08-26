using System.Xml.Linq;
using LupiraDavApi.Backends;
using static LupiraDavApi.Dav.DavXml;

namespace LupiraDavApi.Dav;

/// <summary>
/// The unified CalDAV (RFC 4791) + CardDAV (RFC 6352) gateway over the three /dav-backend upstreams:
/// one principal home serves VEVENT calendars (cal), VTODO lists (tasks), and address books (contact).
/// Stateless — every read/write is a pass-through to the owning backend (picked from the path marker);
/// ETags, sync tokens, and blobs cross verbatim. A backend failure during any enumeration fails the
/// whole response with 503 — never a partial listing (clients delete local collections that vanish).
/// A principal addresses only its own /u/{email}/ tree.
/// </summary>
public static class DavRouter
{
    public static async Task Handle(HttpContext ctx)
    {
        var method = ctx.Request.Method.ToUpperInvariant();
        var ct = ctx.RequestAborted;

        if (method == "OPTIONS")
        {
            WriteOptions(ctx);
            return;
        }

        if (method is "MKCALENDAR" or "MKCOL" or "PROPPATCH" or "MOVE" or "COPY" or "LOCK" or "UNLOCK")
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var email = ctx.User.FindFirst("email")?.Value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var target = DavPath.Parse(ctx.Request.Path.Value ?? string.Empty);
        if (target.Kind == DavTargetKind.Unknown)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (target.Email is not null && !string.Equals(target.Email, email, StringComparison.Ordinal))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;   // two-account guard: own tree only
            return;
        }

        var backends = ctx.RequestServices.GetRequiredService<DavBackendRegistry>();
        var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        var depth = ctx.Request.Headers.TryGetValue("Depth", out var dh) ? dh.ToString() : "0";
        var deep = depth is "1" or "infinity";

        try
        {
            switch (target.Kind)
            {
                case DavTargetKind.Root when method == "PROPFIND":
                    await WriteMultiStatus(ctx, MultiStatus(
                        Response(
                            $"{baseUrl}/dav/",
                            new XElement(D + "resourcetype", new XElement(D + "collection")),
                            new XElement(D + "current-user-principal", Href(DavPath.PrincipalHref(baseUrl, email))))));
                    return;

                case DavTargetKind.Principal when method == "PROPFIND":
                    await WriteMultiStatus(ctx, MultiStatus(
                        Response(
                            DavPath.PrincipalHref(baseUrl, email),
                            new XElement(D + "resourcetype", new XElement(D + "collection"), new XElement(D + "principal")),
                            new XElement(D + "displayname", email),
                            new XElement(D + "current-user-principal", Href(DavPath.PrincipalHref(baseUrl, email))),
                            new XElement(D + "principal-URL", Href(DavPath.PrincipalHref(baseUrl, email))),
                            new XElement(C + "calendar-home-set", Href(DavPath.CalendarHomeHref(baseUrl, email))),
                            new XElement(CR + "addressbook-home-set", Href(DavPath.AddressBookHomeHref(baseUrl, email))))));
                    return;

                case DavTargetKind.CalendarHome when method == "PROPFIND":
                    await CalendarHomePropfind(ctx, backends, baseUrl, email, deep, ct);
                    return;

                case DavTargetKind.AddressBookHome when method == "PROPFIND":
                    await AddressBookHomePropfind(ctx, backends, baseUrl, email, deep, ct);
                    return;

                case DavTargetKind.Collection:
                    {
                        var backend = backends.Get(target.Backend!);
                        if (method == "PROPFIND")
                        {
                            await CollectionPropfind(ctx, backend, target, baseUrl, email, deep, ct);
                            return;
                        }

                        if (method == "REPORT")
                        {
                            await HandleReport(ctx, backend, target, baseUrl, email, ct);
                            return;
                        }

                        break;
                    }

                case DavTargetKind.Resource:
                    {
                        var backend = backends.Get(target.Backend!);
                        if (method is "GET" or "HEAD")
                        {
                            await GetResource(ctx, backend, target, email, ct);
                            return;
                        }

                        if (method == "PUT")
                        {
                            await PutResource(ctx, backend, target, email, ct);
                            return;
                        }

                        if (method == "DELETE")
                        {
                            await DeleteResource(ctx, backend, target, email, ct);
                            return;
                        }

                        break;
                    }
            }

            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        }
        catch (DavBackendUnavailableException ex)
        {
            // Never emit a partial home/listing — clients treat a missing collection as deleted.
            ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DavRouter))
                .LogWarning(ex, "DAV request failed: backend {Backend} unavailable.", ex.Backend);
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
    }

    private static async Task CalendarHomePropfind(HttpContext ctx, DavBackendRegistry backends, string baseUrl, string email, bool deep, CancellationToken ct)
    {
        var responses = new List<XElement>
        {
            Response(DavPath.CalendarHomeHref(baseUrl, email), new XElement(D + "resourcetype", new XElement(D + "collection"))),
        };
        if (deep)
        {
            // Both kinds live in the one calendar home; any failure → 503 for the whole response.
            var cal = backends.Cal.CollectionsAsync(email, ct);
            var tasks = backends.Tasks.CollectionsAsync(email, ct);
            await Task.WhenAll(cal, tasks);
            foreach (var (backend, dto) in new[] { ("cal", cal.Result), ("tasks", tasks.Result) })
                foreach (var c in dto.Collections)
                    responses.Add(Response(DavPath.CollectionHref(baseUrl, email, backend, c.Id), CalendarProps(c)));
        }

        await WriteMultiStatus(ctx, MultiStatus([.. responses]));
    }

    private static async Task AddressBookHomePropfind(HttpContext ctx, DavBackendRegistry backends, string baseUrl, string email, bool deep, CancellationToken ct)
    {
        var responses = new List<XElement>
        {
            Response(DavPath.AddressBookHomeHref(baseUrl, email), new XElement(D + "resourcetype", new XElement(D + "collection"))),
        };
        if (deep)
        {
            var dto = await backends.Contact.CollectionsAsync(email, ct);
            foreach (var a in dto.Collections)
                responses.Add(Response(DavPath.CollectionHref(baseUrl, email, "contact", a.Id), AddressbookProps(a)));
        }

        await WriteMultiStatus(ctx, MultiStatus([.. responses]));
    }

    private static async Task CollectionPropfind(HttpContext ctx, IDavBackend backend, DavTarget target, string baseUrl, string email, bool deep, CancellationToken ct)
    {
        // Collection props come off the collections listing (the contract has no per-collection GET).
        var all = await backend.CollectionsAsync(email, ct);
        var col = all.Collections.FirstOrDefault(c => c.Id == target.CollectionId);
        if (col is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var isCard = target.Backend == "contact";
        var responses = new List<XElement>
        {
            Response(
                DavPath.CollectionHref(baseUrl, email, target.Backend!, col.Id),
                isCard ? AddressbookProps(col) : CalendarProps(col)),
        };
        if (deep)
        {
            var resources = await backend.QueryAsync(email, target.CollectionId, new DavQueryRequest(), ct);
            foreach (var r in resources?.Resources ?? [])
            {
                responses.Add(Response(
                    DavPath.ResourceHref(baseUrl, email, target.Backend!, col.Id, r.Uid),
                    new XElement(D + "getetag", Etag(r.Etag)),
                    new XElement(D + "getcontenttype", isCard ? "text/vcard; charset=utf-8" : "text/calendar; charset=utf-8")));
            }
        }

        await WriteMultiStatus(ctx, MultiStatus([.. responses]));
    }

    private static async Task HandleReport(HttpContext ctx, IDavBackend backend, DavTarget target, string baseUrl, string email, CancellationToken ct)
    {
        var body = await ReadBody(ctx);
        var doc = DavProtocol.TryParseXml(body);
        var isCard = target.Backend == "contact";

        if (doc?.Root?.Name == D + "sync-collection")
        {
            var since = DavProtocol.ParseSyncToken(doc);
            var changes = await backend.ChangesAsync(email, target.CollectionId, since, ct);
            if (changes is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var responses = new List<XElement>();
            foreach (var c in changes.Changed)
            {
                responses.Add(Response(
                    DavPath.ResourceHref(baseUrl, email, target.Backend!, target.CollectionId, c.Uid),
                    new XElement(D + "getetag", Etag(c.Etag))));
            }

            foreach (var uid in changes.Deleted)
                responses.Add(DeletedResponse(DavPath.ResourceHref(baseUrl, email, target.Backend!, target.CollectionId, uid)));
            await WriteMultiStatus(ctx, MultiStatusWithToken(changes.SyncToken, [.. responses]));
            return;
        }

        // calendar-multiget / addressbook-multiget (href uids) or calendar-query (optional time-range).
        var query = new DavQueryRequest { IncludeContent = true };
        var requested = DavProtocol.ExtractHrefUids(body, isCard ? ".vcf" : ".ics");
        if (requested.Count > 0) query.Uids = requested;
        else if (DavProtocol.ParseTimeRange(doc) is { } range) (query.Start, query.End) = range;

        var result = await backend.QueryAsync(email, target.CollectionId, query, ct);
        if (result is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var dataName = isCard ? CR + "address-data" : C + "calendar-data";
        await WriteMultiStatus(ctx, MultiStatus([.. result.Resources.Select(r =>
            Response(
                DavPath.ResourceHref(baseUrl, email, target.Backend!, target.CollectionId, r.Uid),
                new XElement(D + "getetag", Etag(r.Etag)),
                new XElement(dataName, r.Content)))]));
    }

    private static async Task GetResource(HttpContext ctx, IDavBackend backend, DavTarget target, string email, CancellationToken ct)
    {
        var blob = await backend.GetResourceAsync(email, target.CollectionId, target.Uid!, ct);
        if (blob is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ctx.Response.Headers.ETag = Etag(blob.Etag);
        ctx.Response.ContentType = $"{blob.ContentType}; charset=utf-8";
        if (ctx.Request.Method == "HEAD") return;
        await ctx.Response.WriteAsync(blob.Content, ct);
    }

    private static async Task PutResource(HttpContext ctx, IDavBackend backend, DavTarget target, string email, CancellationToken ct)
    {
        var raw = await ReadBody(ctx);
        var (ifMatch, ifNoneMatchStar) = Preconditions(ctx);
        var contentType = target.Backend == "contact" ? "text/vcard" : "text/calendar";

        // Single attempt, no retries — the DAV client owns protocol-level retry, and the backend's
        // UID + ETag precondition semantics make that retry safe.
        var outcome = await backend.PutResourceAsync(email, target.CollectionId, target.Uid!, raw, contentType, ifMatch, ifNoneMatchStar, ct);
        if (outcome.Etag is { } etag) ctx.Response.Headers.ETag = Etag(etag);
        ctx.Response.StatusCode = outcome.Status;
    }

    private static async Task DeleteResource(HttpContext ctx, IDavBackend backend, DavTarget target, string email, CancellationToken ct)
    {
        var (ifMatch, _) = Preconditions(ctx);
        ctx.Response.StatusCode = await backend.DeleteResourceAsync(email, target.CollectionId, target.Uid!, ifMatch, ct);
    }

    private static (string? IfMatch, bool IfNoneMatchStar) Preconditions(HttpContext ctx)
    {
        var ifMatch = ctx.Request.Headers.TryGetValue("If-Match", out var im) && im.Count > 0 ? im.ToString() : null;
        var ifNoneMatch = ctx.Request.Headers.TryGetValue("If-None-Match", out var n) ? n.ToString() : null;
        return DavProtocol.ParsePreconditions(ifMatch, ifNoneMatch);
    }

    private static void WriteOptions(HttpContext ctx)
    {
        ctx.Response.Headers["DAV"] = "1, 2, 3, calendar-access, addressbook";
        ctx.Response.Headers["Allow"] = "OPTIONS, GET, HEAD, PUT, DELETE, PROPFIND, REPORT";
        ctx.Response.StatusCode = StatusCodes.Status200OK;
    }

    private static async Task WriteMultiStatus(HttpContext ctx, XElement multistatus)
    {
        ctx.Response.StatusCode = 207;
        ctx.Response.ContentType = "application/xml; charset=utf-8";
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), multistatus);
        await ctx.Response.WriteAsync(doc.Declaration + "\n" + doc.ToString(SaveOptions.DisableFormatting), ctx.RequestAborted);
    }

    private static async Task<string> ReadBody(HttpContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.Body);
        return await reader.ReadToEndAsync(ctx.RequestAborted);
    }
}
