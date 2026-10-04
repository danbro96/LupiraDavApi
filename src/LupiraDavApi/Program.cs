using Lupira.Clients.ServiceTokens;
using Lupira.Depz;
using Lupira.Hosting.Defaults;
using Lupira.Hosting.Health;
using Lupira.Hosting.Observability;
using LupiraDavApi.Auth;
using LupiraDavApi.Backends;
using LupiraDavApi.Dav;
using LupiraDavApi.Dependencies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// --- Upstreams: the three /dav-backend services (cal = VEVENT, tasks = VTODO, contact = vCard).
//     One confidential client (ServiceAuth) mints per-backend-audience bearers via scopes. ---
builder.Services.Configure<BackendsOptions>(builder.Configuration.GetSection(BackendsOptions.SectionName));
builder.Services.Configure<ServiceAuthOptions>(builder.Configuration.GetSection(ServiceAuthOptions.SectionName));

var backendsConfig = builder.Configuration.GetSection(BackendsOptions.SectionName).Get<BackendsOptions>() ?? new BackendsOptions();
RegisterBackend("cal", backendsConfig.Cal);
RegisterBackend("tasks", backendsConfig.Tasks);
RegisterBackend("contact", backendsConfig.Contact);
builder.Services.AddSingleton<DavBackendRegistry>();

// Non-gating dependency probe (/depz): edges derive from the options above, probed on a dedicated client.
var serviceAuthConfig = builder.Configuration.GetSection(ServiceAuthOptions.SectionName).Get<ServiceAuthOptions>() ?? new ServiceAuthOptions();
builder.Services.AddLupiraDepz(o =>
{
    builder.Configuration.GetSection(DepzOptions.SectionName).Bind(o);
    o.ServiceName = "lupira-dav-api";
    o.MeterName = "LupiraDavApi.Depz";
    o.MetricPrefix = "dav";
});
builder.Services.AddLupiraDepzTargets(DependencyTargets.From(backendsConfig, serviceAuthConfig));

void RegisterBackend(string name, BackendOptions opts)
{
    builder.Services.AddHttpClient($"dav-backend-{name}", c =>
    {
        if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
            c.BaseAddress = new Uri(opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + "/");
    }).AddLupiraServiceToken(sp =>
    {
        var auth = sp.GetRequiredService<IOptions<ServiceAuthOptions>>().Value;
        return new OutboundHopOptions
        {
            BaseUrl = opts.BaseUrl,
            TokenUrl = auth.TokenUrl,
            ClientId = auth.ClientId,
            ClientSecret = auth.ClientSecret,
            Scope = opts.Scope,
        };
    });
    builder.Services.AddSingleton<IDavBackend>(sp => new DavBackendClient(
        name,
        sp.GetRequiredService<IHttpClientFactory>().CreateClient($"dav-backend-{name}")));
}

// --- Auth: HTTP Basic → Authentik LDAP outpost. The gateway verifies the human credential on every
//     request, then asserts the acting user to the backends via its service identity. ---
builder.Services.AddAuthentication(DavConstants.Scheme)
    .AddScheme<AuthenticationSchemeOptions, DavBasicAuthHandler>(DavConstants.Scheme, _ => { });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("DavPolicy", p => p.AddAuthenticationSchemes(DavConstants.Scheme).RequireAuthenticatedUser());

builder.AddLupiraTelemetry("lupira-dav-api");

// No dependency pings on /readyz: a gateway must not cascade backend restarts.
builder.Services.AddLupiraHealth();

// Behind the Cloudflare Tunnel the public host differs from the container, so honor forwarded headers —
// DAV discovery must emit absolute https://dav-api.lupira.com/... hrefs, or clients loop on container URLs.
builder.AddLupiraDefaults(o =>
{
    o.StrictNumbers = false;
    o.CaseInsensitiveProperties = true;
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.StatusCodePages = false;
});

var app = builder.Build();

app.UseLupiraDefaults();

app.UseAuthentication();
app.UseAuthorization();

// Health probes (self-only).
app.MapLupiraHealth();
app.MapDepz();

// DAV service discovery (anonymous): clients probe these before auth, then follow to /dav/.
app.MapMethods("/.well-known/caldav", ["GET", "PROPFIND", "OPTIONS"], () => Results.Redirect("/dav/", permanent: true));
app.MapMethods("/.well-known/carddav", ["GET", "PROPFIND", "OPTIONS"], () => Results.Redirect("/dav/", permanent: true));

// The unified CalDAV/CardDAV catch-all (Basic auth). All HTTP verbs — including PROPFIND/REPORT —
// reach DavRouter, which dispatches on the method. The cast picks the RequestDelegate Map overload.
app.Map("/dav/{**path}", (RequestDelegate) DavRouter.Handle).RequireAuthorization("DavPolicy");

app.Run();

// Exposes the implicit Program entry point to the integration test assembly (WebApplicationFactory<Program>).
public partial class Program;
