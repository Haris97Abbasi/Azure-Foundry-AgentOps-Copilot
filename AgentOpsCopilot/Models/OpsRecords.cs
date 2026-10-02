namespace AgentOpsCopilot.Models;

public sealed record Incident(
    string Id,
    string Service,
    string Severity,
    string Symptoms,
    DateTimeOffset StartTime,
    string Status);

public sealed record ChangeRecord(
    string Id,
    string Service,
    string Type,
    string Description,
    string Version,
    string Author,
    DateTimeOffset DeployedAt);

public sealed record RunbookSnippet(
    string File,
    string Snippet,
    int Score);
