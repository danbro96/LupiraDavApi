using System.Text.Json.Serialization;

namespace LupiraDavApi.Dependencies;

/// <summary>Outcome of one edge probe. Unauthorized (downstream rejected our token) and
/// NoCredential (we couldn't mint one) are deliberately not Down.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DependencyStatus>))]
public enum DependencyStatus
{
    Unknown,
    Healthy,
    Degraded,
    Unauthorized,
    Down,
    Unconfigured,
    NoCredential,
}
