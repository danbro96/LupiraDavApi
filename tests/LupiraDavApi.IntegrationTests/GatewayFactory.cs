using System.Net.Http.Headers;
using System.Text;
using LupiraDavApi.Backends;
using LupiraDavApi.IntegrationTests.Stubs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LupiraDavApi.IntegrationTests;

/// <summary>
/// Hosts the gateway with the three upstreams replaced by in-process <see cref="StubDavBackend"/>s —
/// the house pattern for stateless proxies (no network, no containers). Runs in <c>Development</c> so
/// Basic auth accepts any password (no LDAP). Each test class gets a fresh factory + fresh stubs.
/// </summary>
public sealed class GatewayFactory : WebApplicationFactory<Program>
{
    public StubDavBackend CalStub { get; } = new("cal", DavCollectionKind.EventCalendar);
    public StubDavBackend TasksStub { get; } = new("tasks", DavCollectionKind.TodoList);
    public StubDavBackend ContactStub { get; } = new("contact", DavCollectionKind.AddressBook);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDavBackend>();
            services.AddSingleton<IDavBackend>(CalStub);
            services.AddSingleton<IDavBackend>(TasksStub);
            services.AddSingleton<IDavBackend>(ContactStub);
        });
    }

    /// <summary>A client authenticated for /dav (HTTP Basic; Development accepts any password).</summary>
    public HttpClient DavClient(string email)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:x"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
        return client;
    }

    public HttpClient AnonymousClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
