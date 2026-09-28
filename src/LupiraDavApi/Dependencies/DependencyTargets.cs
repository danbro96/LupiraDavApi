using LupiraDavApi.Backends;

namespace LupiraDavApi.Dependencies;

/// <summary>Roster derived from the same options the real clients bind — edges cannot drift.</summary>
public static class DependencyTargets
{
    public static IReadOnlyList<DependencyTarget> From(BackendsOptions backends, ServiceAuthOptions auth) =>
    [
        Target("lupira-cal-api", backends.Cal, auth),
        Target("lupira-tasks-api", backends.Tasks, auth),
        Target("lupira-contact-api", backends.Contact, auth),
    ];

    private static DependencyTarget Target(string name, BackendOptions backend, ServiceAuthOptions auth) => new()
    {
        Name = name,
        BaseUrl = backend.BaseUrl,
        ProbePath = "pingz",
        TokenUrl = auth.TokenUrl,
        ClientId = auth.ClientId,
        ClientSecret = auth.ClientSecret,
        Scope = backend.Scope,
    };
}
