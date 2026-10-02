using System.Text.Encodings.Web;
using System.Text.Json;
using AgentOpsCopilot.Models;

namespace AgentOpsCopilot.Tools;

public sealed class MockData
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly TimeSpan NewestRecordAge = TimeSpan.FromMinutes(30);

    public IReadOnlyList<Incident> Incidents { get; }
    public IReadOnlyList<ChangeRecord> Changes { get; }

    public MockData(string dataDirectory, TimeProvider? clock = null)
    {
        var incidents = Read<Incident>(Path.Combine(dataDirectory, "incidents.json"));
        var changes = Read<ChangeRecord>(Path.Combine(dataDirectory, "changes.json"));

        var newest = incidents.Select(i => i.StartTime).Concat(changes.Select(c => c.DeployedAt)).DefaultIfEmpty().Max();
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var offset = TimeSpan.FromMinutes(Math.Round((now - NewestRecordAge - newest).TotalMinutes));

        Incidents = incidents.Select(i => i with { StartTime = i.StartTime + offset }).ToList();
        Changes = changes.Select(c => c with { DeployedAt = c.DeployedAt + offset }).ToList();
    }

    public static bool ServiceMatches(string service, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var s = Normalize(service);
        var q = Normalize(query);
        return q.Length > 0 && (s.Contains(q) || q.Contains(s));
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static List<T> Read<T>(string path) =>
        JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), Json) ?? [];
}
