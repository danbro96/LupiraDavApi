using LupiraDavApi.Auth;
using LupiraDavApi.Backends;
using LupiraDavApi.Dav;
using LupiraDavApi.Dependencies;
using LupiraDavApi.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// --- Upstreams: the three /dav-backend services (cal = VEVENT, tasks = VTODO, contact = vCard).
//     One confidential client (ServiceAuth) mints per-backend-audience bearers via scopes. ---
builder.Services.Configure<BackendsOptions>(builder.Configuration.GetSection(BackendsOptions.SectionName));
builder.Services.Configure<ServiceAuthOptions>(builder.Configuration.GetSection(ServiceAuthOptions.SectionName));
builder.Services.AddHttpClient(nameof(ServiceTokenProvider));
builder.Services.AddSingleton<ServiceTokenProvider>();

var backendsConfig = builder.Configuration.GetSection(BackendsOptions.SectionName).Get<BackendsOptions>() ?? new BackendsOptions();
RegisterBackend("cal", backendsConfig.Cal);
RegisterBackend("tasks", backendsConfig.Tasks);
RegisterBackend("contact", backendsConfig.Contact);
builder.Services.AddSingleton<DavBackendRegistry>();

// Non-gating dependency probe (/depz): edges derive from the options above, probed on a dedicated client.
var serviceAuthConfig = builder.Configuration.GetSection(ServiceAuthOptions.SectionName).Get<ServiceAuthOptions>() ?? new ServiceAuthOptions();
builder.Services.Configure<DepzOptions>(builder.Configuration.GetSection(DepzOptions.SectionName));
var depzOptions = builder.Configuration.GetSection(DepzOptions.SectionName).Get<DepzOptions>() ?? new DepzOptions();
builder.Services.AddSingleton(DependencyTargets.From(backendsConfig, serviceAuthConfig));
builder.Services.AddSingleton<DependencyReportCache>();
builder.Services.AddSingleton<DependencyProbe>();
builder.Services.AddHttpClient(DependencyProbe.ProbeClientName, c => c.Timeout = depzOptions.ProbeTimeout);
if (depzOptions.Enabled)
    builder.Services.AddHostedService<DependencyPollWorker>();

void RegisterBackend(string name, BackendOptions opts)
{
    builder.Services.AddHttpClient($"dav-backend-{name}", c =>
    {
        if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
            c.BaseAddress = new Uri(opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + "/");
    });
    builder.Services.AddSingleton<IDavBackend>(sp => new DavBackendClient(
        name,
        sp.GetRequiredService<IHttpClientFactory>().CreateClient($"dav-backend-{name}"),
        opts.Scope,
        sp.GetRequiredService<ServiceTokenProvider>()));
}

// --- Auth: HTTP Basic → Authentik LDAP outpost. The gateway verifies the human credential on every
//     request, then asserts the acting user to the backends via its service identity. ---
builder.Services.AddAuthentication(DavConstants.Scheme)
    .AddScheme<AuthenticationSchemeOptions, DavBasicAuthHandler>(DavConstants.Scheme, _ => { });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("DavPolicy", p => p.AddAuthenticationSchemes(DavConstants.Scheme).RequireAuthenticatedUser());

// --- Observability: OpenTelemetry -> OpenObserve. Env-gated. ---
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("lupira-dav-api"))
    .WithTracing(t =>
    {
        // Health probes are polled constantly by docker + devops-monitor; their spans add nothing.
        t.AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
            ctx.Request.Path != "/livez" && ctx.Request.Path != "/readyz" && ctx.Request.Path != "/depz");
        t.AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint)) t.AddOtlpExporter();
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation();
        m.AddHttpClientInstrumentation();
        m.AddRuntimeInstrumentation();
        m.AddMeter("LupiraDavApi.*");
        if (!string.IsNullOrWhiteSpace(otlpEndpoint)) m.AddOtlpExporter();
    });

builder.Logging.AddOpenTelemetry(o =>
{
    o.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("lupira-dav-api"));
    o.IncludeScopes = true;
    o.IncludeFormattedMessage = true;
    if (!string.IsNullOrWhiteSpace(otlpEndpoint)) o.AddOtlpExporter();
});

// No dependency pings on /readyz: a gateway must not cascade backend restarts.
builder.Services.AddHealthChecks();

var app = builder.Build();

// Behind the Cloudflare Tunnel the public host differs from the container, so honor forwarded headers —
// DAV discovery must emit absolute https://dav-api.lupira.com/... hrefs, or clients loop on container URLs.
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
};
forwarded.KnownIPNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);

app.UseAuthentication();
app.UseAuthorization();

// Health probes (self-only).
app.MapHealthChecks("/livez", new HealthCheckOptions { Predicate = _ => false })
    .DisableHttpMetrics();
app.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = _ => false })
    .DisableHttpMetrics();
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
