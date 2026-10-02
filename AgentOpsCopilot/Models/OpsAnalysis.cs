namespace AgentOpsCopilot.Models;

public sealed record OpsAnalysis(
    string Summary,
    string Severity,
    List<string> Evidence,
    List<string> RecommendedActions,
    List<string> Sources,
    bool NeedsHumanEscalation);
