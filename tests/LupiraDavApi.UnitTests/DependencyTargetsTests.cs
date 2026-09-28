using LupiraDavApi.Backends;
using LupiraDavApi.Dependencies;
using Xunit;

namespace LupiraDavApi.UnitTests;

public sealed class DependencyTargetsTests
{
    private static readonly BackendsOptions Backends = new()
    {
        Cal = new BackendOptions { BaseUrl = "http://cal:8080", Scope = "lupira-cal-aud" },
        Tasks = new BackendOptions { BaseUrl = "http://tasks:8080", Scope = "lupira-tasks-aud" },
        Contact = new BackendOptions { BaseUrl = "http://contact:8080", Scope = "lupira-contact-aud" },
    };

    private static readonly ServiceAuthOptions Auth = new()
    {
        TokenUrl = "https://auth/token/",
        ClientId = "lupira-dav-svc",
        ClientSecret = "s3cret",
    };

    [Fact]
    public void One_pingz_target_per_backend_named_by_registry_service()
    {
        var targets = DependencyTargets.From(Backends, Auth);

        Assert.Equal(["lupira-cal-api", "lupira-tasks-api", "lupira-contact-api"], targets.Select(t => t.Name));
        Assert.All(targets, t => Assert.Equal("pingz", t.ProbePath));
    }

    [Fact]
    public void Each_target_gets_its_backend_url_and_scope()
    {
        var targets = DependencyTargets.From(Backends, Auth);

        Assert.Equal(["http://cal:8080", "http://tasks:8080", "http://contact:8080"], targets.Select(t => t.BaseUrl));
        Assert.Equal(["lupira-cal-aud", "lupira-tasks-aud", "lupira-contact-aud"], targets.Select(t => t.Scope));
    }

    [Fact]
    public void All_targets_share_the_gateway_service_credential()
    {
        Assert.All(DependencyTargets.From(Backends, Auth), t =>
        {
            Assert.Equal("https://auth/token/", t.TokenUrl);
            Assert.Equal("lupira-dav-svc", t.ClientId);
            Assert.Equal("s3cret", t.ClientSecret);
            Assert.Null(t.DevUser);
        });
    }

    [Fact]
    public void Unset_options_yield_unconfigured_targets()
    {
        Assert.All(DependencyTargets.From(new BackendsOptions(), new ServiceAuthOptions()), t =>
        {
            Assert.Equal(string.Empty, t.BaseUrl);
            Assert.Null(t.TokenUrl);
        });
    }
}
