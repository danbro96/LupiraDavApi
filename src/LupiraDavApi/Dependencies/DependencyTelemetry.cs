using System.Diagnostics.Metrics;

namespace LupiraDavApi.Dependencies;

internal static class DependencyTelemetry
{
    private static readonly Meter Meter = new("LupiraDavApi.Depz");

    private static readonly Histogram<double> ProbeDuration =
        Meter.CreateHistogram<double>("dav.dependency.probe.duration", unit: "s");

    public static void Record(string dependency, DependencyStatus status, double? latencyMs)
    {
        if (latencyMs is { } ms)
        {
            ProbeDuration.Record(ms / 1000d, new KeyValuePair<string, object?>("dependency", dependency),
                new KeyValuePair<string, object?>("status", status.ToString()));
        }
    }

    public static void ObserveUpFrom(DependencyReportCache cache) =>
        Meter.CreateObservableGauge("dav.dependency.up", () =>
            cache.Current().Dependencies.Select(d => new Measurement<int>(
                d.Status == DependencyStatus.Healthy ? 1 : 0,
                new KeyValuePair<string, object?>("dependency", d.Name),
                new KeyValuePair<string, object?>("status", d.Status.ToString()))));
}
