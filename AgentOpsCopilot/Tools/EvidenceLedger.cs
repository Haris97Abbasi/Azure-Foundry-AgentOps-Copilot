namespace AgentOpsCopilot.Tools;

public sealed record ToolCall(string Tool, string Arguments, IReadOnlyList<string> ReturnedSources);

public sealed class EvidenceLedger
{
    private readonly List<ToolCall> _calls = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<ToolCall> Calls
    {
        get { lock (_lock) return _calls.ToList(); }
    }

    public IReadOnlySet<string> Sources
    {
        get { lock (_lock) return _calls.SelectMany(c => c.ReturnedSources).ToHashSet(StringComparer.OrdinalIgnoreCase); }
    }

    public void Record(string tool, string arguments, IEnumerable<string> returnedSources)
    {
        lock (_lock) _calls.Add(new ToolCall(tool, arguments, returnedSources.ToList()));
    }

    public void Clear()
    {
        lock (_lock) _calls.Clear();
    }
}
