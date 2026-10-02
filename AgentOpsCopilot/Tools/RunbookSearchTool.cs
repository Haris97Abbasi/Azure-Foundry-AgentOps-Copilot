using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentOpsCopilot.Models;

namespace AgentOpsCopilot.Tools;

public sealed partial class RunbookSearchTool
{
    private const int MaxResults = 3;
    private const int MaxHitsPerTerm = 3;
    private const int HeadingBonus = 2;
    private const int FileNameBonus = 3;

    private static readonly HashSet<string> StopWords =
    [
        "a", "an", "the", "and", "or", "to", "of", "in", "on", "for", "is", "are", "was", "be", "it", "this", "that",
        "what", "how", "why", "should", "do", "does", "i", "we", "my", "our", "with", "from", "after", "today",
        "has", "have", "started", "returning", "any", "me", "can", "you"
    ];

    private sealed record Section(string File, string Heading, string Text, HashSet<string> HeadingTerms, List<string> BodyTerms, HashSet<string> FileTerms);

    private readonly List<Section> _sections;
    private readonly EvidenceLedger _ledger;

    public RunbookSearchTool(string knowledgeDirectory, EvidenceLedger ledger)
    {
        _ledger = ledger;
        _sections = Directory.EnumerateFiles(knowledgeDirectory, "*.md").Order().SelectMany(LoadSections).ToList();
    }

    [Description("Searches the internal runbooks and incident policy (SOPs, dependency checks, rollback criteria, " +
                 "severity definitions, escalation rules). Returns the top 1-3 matching snippets with their file names.")]
    public string SearchRunbooks(
        [Description("Keywords describing the problem or procedure, e.g. 'checkout 500 errors rollback'.")]
        string query)
    {
        var terms = QueryTerms(query);
        var results = _sections
            .Select(s => new RunbookSnippet(s.File, s.Text, Score(s, terms)))
            .Where(r => r.Score > 0)
            .OrderByDescending(r => r.Score)
            .Take(MaxResults)
            .ToList();

        _ledger.Record(nameof(SearchRunbooks), query, results.Select(r => r.File).Distinct());

        return JsonSerializer.Serialize(new
        {
            results,
            note = results.Count == 0 ? $"No runbook content matched '{query}'." : null
        }, MockData.Json);
    }

    private static int Score(Section section, HashSet<string> terms) =>
        terms.Sum(t =>
            Math.Min(section.BodyTerms.Count(w => w == t), MaxHitsPerTerm)
            + (section.HeadingTerms.Contains(t) ? HeadingBonus : 0)
            + (section.FileTerms.Contains(t) ? FileNameBonus : 0));

    private static HashSet<string> QueryTerms(string query)
    {
        var terms = Tokenize(query).Where(t => !StopWords.Contains(t)).ToHashSet();
        // Runbooks talk about "5xx"; users usually say "500" or "503".
        if (terms.Any(t => t.Length == 3 && t[0] == '5' && t.All(char.IsDigit))) terms.Add("5xx");
        return terms;
    }

    private static IEnumerable<Section> LoadSections(string path)
    {
        var file = Path.GetFileName(path);
        var fileTerms = Tokenize(Path.GetFileNameWithoutExtension(path)).ToHashSet();
        string? heading = null;
        var body = new List<string>();

        foreach (var line in File.ReadLines(path).Append("## "))
        {
            if (!line.StartsWith("## ")) { if (heading is not null && line.Trim().Length > 0) body.Add(line.Trim()); continue; }

            if (heading is not null && body.Count > 0)
            {
                var text = string.Join("\n", body);
                yield return new Section(file, heading, $"{heading}\n{text}", Tokenize(heading).ToHashSet(), Tokenize(text).ToList(), fileTerms);
            }
            heading = line[3..].Trim();
            body.Clear();
        }
    }

    private static IEnumerable<string> Tokenize(string text) =>
        WordRegex().Matches(text.ToLowerInvariant()).Select(m => Stem(m.Value));

    // Light stemming so "timeouts" matches "timeout" and "errors" matches "error".
    private static string Stem(string word) =>
        word.Length > 4 && word.EndsWith('s') && !word.EndsWith("ss") ? word[..^1] : word;

    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex WordRegex();
}
