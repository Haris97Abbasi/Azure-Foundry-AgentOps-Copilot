using System.ClientModel;
using System.Text.RegularExpressions;
using AgentOpsCopilot.Models;

namespace AgentOpsCopilot.Guardrails;

public sealed record ValidatedAnalysis(OpsAnalysis Analysis, IReadOnlyList<string> Notes);

public sealed partial class SafetyValidator
{
    private const string ApprovalPrefix = "Requires human approval: ";

    public string? CheckInput(string question)
    {
        if (InvestigativeQuestion().IsMatch(question)) return null;

        foreach (var pattern in DestructivePatterns)
        {
            var match = pattern.Match(question);
            if (match.Success) return $"it asks for a destructive or security-weakening action (\"{match.Value.Trim()}\")";
        }
        return null;
    }

    public OpsAnalysis Refusal(string reason) => new(
        Summary: $"Request declined: {reason}. OpsCopilot never performs or plans destructive changes; it only recommends read-only investigation and safe escalation.",
        Severity: "Unknown",
        Evidence: [],
        RecommendedActions:
        [
            "Describe the underlying problem you are trying to solve so it can be investigated safely.",
            "If a destructive or security-related change is truly required, raise a change request and get approval from the on-call incident commander.",
            "Follow the escalation expectations in the incident policy."
        ],
        Sources: [],
        NeedsHumanEscalation: true);

    public OpsAnalysis ContentFilterRefusal() => new(
        Summary: "The request was blocked by the Microsoft Foundry content filter (DefaultV2 guardrail). Please rephrase it as an operational question.",
        Severity: "Unknown",
        Evidence: [],
        RecommendedActions: ["Rephrase the question to focus on the service, symptoms and timeline."],
        Sources: [],
        NeedsHumanEscalation: false);

    public static bool IsContentFilterBlock(ClientResultException exception) =>
        exception.Message.Contains("content_filter", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("ResponsibleAIPolicyViolation", StringComparison.OrdinalIgnoreCase);

    public ValidatedAnalysis ValidateOutput(OpsAnalysis analysis, IReadOnlySet<string> toolSources, IEnumerable<string> earlierAnswers)
    {
        var notes = new List<string>();
        var known = new HashSet<string>(toolSources, StringComparer.OrdinalIgnoreCase);
        foreach (var answer in earlierAnswers)
            foreach (Match m in SourceToken().Matches(answer)) known.Add(m.Value);

        var sources = new List<string>();
        foreach (var source in analysis.Sources.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()))
        {
            var grounded = known.FirstOrDefault(k => source.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (grounded is null) notes.Add($"Removed source \"{source}\": no tool returned it.");
            else if (!sources.Contains(grounded, StringComparer.OrdinalIgnoreCase)) sources.Add(grounded);
        }

        var evidence = new List<string>();
        var hypotheses = new List<string>();
        foreach (var item in analysis.Evidence.Where(e => !string.IsNullOrWhiteSpace(e)))
        {
            var unknownIds = RecordId().Matches(item).Select(m => m.Value).Where(id => !known.Contains(id)).Distinct().ToList();
            if (unknownIds.Count > 0)
                notes.Add($"Removed evidence citing {string.Join(", ", unknownIds)}: no tool returned it.");
            else if (item.Contains("hypothes", StringComparison.OrdinalIgnoreCase))
                hypotheses.Add(item.Trim());
            else
                evidence.Add(item.Trim());
        }
        if (hypotheses.Count > 0) notes.Add($"Moved {hypotheses.Count} hypothesis item(s) out of evidence into the summary.");

        var severity = NormalizeSeverity(analysis.Severity);
        if (!severity.Equals(analysis.Severity, StringComparison.Ordinal))
            notes.Add($"Severity \"{analysis.Severity}\" normalized to {severity}.");

        var actions = new List<string>();
        foreach (var action in analysis.RecommendedActions.Where(a => !string.IsNullOrWhiteSpace(a)))
        {
            var text = LeadingNumber().Replace(action.Trim(), "");
            if (ChangesProduction(text) && !text.Contains("approv", StringComparison.OrdinalIgnoreCase))
                text = ApprovalPrefix + text;
            actions.Add(text);
        }

        var needsApproval = actions.Any(ChangesProduction);
        var escalate = analysis.NeedsHumanEscalation || severity is "SEV1" or "SEV2" || needsApproval;
        if (escalate && !analysis.NeedsHumanEscalation)
            notes.Add("Human escalation forced: high severity or an action that needs approval.");

        var summary = hypotheses.Count == 0 ? analysis.Summary : $"{analysis.Summary} {string.Join(" ", hypotheses)}";

        return new ValidatedAnalysis(new OpsAnalysis(summary, severity, evidence, actions, sources, escalate), notes);
    }

    public static string NormalizeSeverity(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity)) return "Unknown";

        var numbered = SeverityNumber().Match(severity);
        if (numbered.Success) return $"SEV{(numbered.Groups[1].Success ? numbered.Groups[1].Value : numbered.Groups[2].Value)}";

        return severity.Trim().ToLowerInvariant() switch
        {
            "critical" => "SEV1",
            "high" or "major" => "SEV2",
            "medium" or "moderate" => "SEV3",
            "low" or "minor" => "SEV4",
            _ => "Unknown"
        };
    }

    public static bool ChangesProduction(string action)
    {
        var mainClause = LeadingCondition().Replace(action.Replace(ApprovalPrefix, ""), "");
        return !ReadOnlyStart().IsMatch(mainClause) && Remediation().IsMatch(mainClause);
    }

    private static readonly Regex[] DestructivePatterns =
    [
        DeleteData(),
        WeakenSecurity(),
        DestructiveCommand()
    ];

    [GeneratedRegex(@"\b(delete|drop|truncate|wipe|purge|destroy|erase)\b.{0,40}\b(prod(uction)?|database|db|tables?|data|backups?|customers?|records|cluster|bucket|logs?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DeleteData();

    [GeneratedRegex(@"\b(disable|turn off|switch off|bypass|remove|deactivate)\b.{0,30}\b(security|firewall|waf|mfa|2fa|authentication|auth|authorization|encryption|audit|logging|monitoring|alerts?|alerting|tls|ssl)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WeakenSecurity();

    [GeneratedRegex(@"rm\s+-rf|\bdrop\s+(table|database)\b|\bformat\b.{0,20}\b(disk|drive|server)\b|\bkill\b.{0,20}\b(all|every)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DestructiveCommand();

    [GeneratedRegex(@"^\s*(did|was|were|who|when|why|has|have|had)\b", RegexOptions.IgnoreCase)]
    private static partial Regex InvestigativeQuestion();

    [GeneratedRegex(@"\b(INC|CHG)-\d+\b|\b[\w-]+\.md\b", RegexOptions.IgnoreCase)]
    private static partial Regex SourceToken();

    [GeneratedRegex(@"\b(INC|CHG)-\d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex RecordId();

    [GeneratedRegex(@"\bsev\s*-?\s*([1-4])\b|\bp([1-4])\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeverityNumber();

    [GeneratedRegex(@"^\s*\d+[.)]\s*")]
    private static partial Regex LeadingNumber();

    [GeneratedRegex(@"\b(roll(ing|ed)?\s?back|revert\w*|restart\w*|reboot\w*|redeploy\w*|deploy\w*|delet\w*|drop\w*|truncat\w*|kill\w*|terminat\w*|disabl\w*|scal(e|ing)\s+(up|down|in|out)|fail\s?over|purg\w*|flush\w*|increas\w*|decreas\w*|rais(e|ing)|lower(ing)?|chang(e|ing)|modif\w*|updat(e|ing)|apply\w*|restor\w*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Remediation();

    [GeneratedRegex(@"^\s*(if|after|once|when|before|while)\b[^,]*,\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingCondition();

    [GeneratedRegex(@"^\s*(check|verify|confirm|monitor|review|investigate|inspect|compare|watch|look|query|search|validate|communicate|post|notify|page|escalate|document|open|track|gather|collect|identify|determine)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyStart();
}
