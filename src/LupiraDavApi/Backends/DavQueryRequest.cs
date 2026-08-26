namespace LupiraDavApi.Backends;

public sealed class DavQueryRequest
{
    public List<string>? Uids { get; set; }

    public DateTimeOffset? Start { get; set; }

    public DateTimeOffset? End { get; set; }

    public bool IncludeContent { get; set; }
}
