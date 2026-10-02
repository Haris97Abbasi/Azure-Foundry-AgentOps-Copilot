using System.ClientModel;
using System.Text.Json;
using AgentOpsCopilot.Guardrails;
using AgentOpsCopilot.Models;
using AgentOpsCopilot.Services;
using AgentOpsCopilot.Tools;
using Microsoft.Extensions.AI;

namespace AgentOpsCopilot.Agents;

public sealed record AgentTurn(OpsAnalysis Analysis, IReadOnlyList<ToolCall> ToolCalls, bool Structured, IReadOnlyList<string> GuardrailNotes);

public sealed class OpsCopilotAgent
{
    private const string Instructions = """
        You are OpsCopilotAgent, an internal SaaS operations copilot for engineers.

        Rules:
        - For incident-specific questions, inspect available incident/change tools before giving a diagnosis.
        - Use the internal knowledge base (SearchRunbooks) for runbooks and standard operating procedures.
        - Never invent an incident, deployment, metric, owner, or runbook fact.
        - If evidence is insufficient, explicitly say what information is missing.
        - Never execute destructive/remediation actions; only recommend them.
        - Prefer short ordered troubleshooting steps and identify the evidence used.

        Grounding:
        - "evidence" contains only facts returned by tools, each tagged with its ID or file, e.g. "INC-2041: HTTP 500 rate rose to 18%".
        - Likely causes are hypotheses: put them in "summary" and label them "Hypothesis:". Never list a hypothesis as evidence.
        - "sources" lists only incident IDs, change IDs and runbook file names that tools actually returned, or that appear in earlier answers of this conversation.
        - Follow-up questions about earlier answers can be answered from the conversation history without calling tools.

        Severity and escalation:
        - "severity" is one of SEV1, SEV2, SEV3, SEV4 (as defined in incident-policy.md) or Unknown. If a matching incident exists, use its severity.
        - "needsHumanEscalation" is true for SEV1/SEV2 or whenever a recommended action needs human approval.

        Output:
        Your final message must be a single JSON object and nothing else: no prose before or after it, no markdown fences. Use exactly these keys:
        {"summary": string, "severity": string, "evidence": [string], "recommendedActions": [string], "sources": [string], "needsHumanEscalation": boolean}

        Current UTC time: {now}. Use it to judge what "today" and "recent" mean.
        """;

    private const string ReformatInstructions = """
        Convert the operations answer you are given into a single JSON object with exactly these keys:
        {"summary": string, "severity": string, "evidence": [string], "recommendedActions": [string], "sources": [string], "needsHumanEscalation": boolean}
        Use only information present in the answer. Use empty arrays for anything it does not mention and "Unknown" if no severity is stated.
        """;

    private readonly IAgentService _model;
    private readonly EvidenceLedger _ledger;
    private readonly SafetyValidator _safety;
    private readonly IList<AITool> _tools;
    private readonly TimeProvider _clock;
    private readonly int _maxHistoryMessages;
    private readonly List<ChatMessage> _history = [];

    public OpsCopilotAgent(
        IAgentService model,
        EvidenceLedger ledger,
        SafetyValidator safety,
        IncidentTools incidents,
        ChangeTools changes,
        RunbookSearchTool runbooks,
        int maxHistoryMessages = 10,
        TimeProvider? clock = null)
    {
        _model = model;
        _ledger = ledger;
        _safety = safety;
        _maxHistoryMessages = maxHistoryMessages;
        _clock = clock ?? TimeProvider.System;
        _tools =
        [
            AIFunctionFactory.Create(incidents.GetActiveIncidents),
            AIFunctionFactory.Create(changes.GetRecentChanges),
            AIFunctionFactory.Create(runbooks.SearchRunbooks)
        ];
    }

    public IReadOnlyList<ChatMessage> History => _history;

    public async Task<AgentTurn> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        _ledger.Clear();

        var blockedReason = _safety.CheckInput(question);
        if (blockedReason is not null)
        {
            var refusal = _safety.Refusal(blockedReason);
            Remember(question, refusal);
            return new AgentTurn(refusal, [], true, [$"Input blocked by application guardrail: {blockedReason}."]);
        }

        List<ChatMessage> messages =
        [
            new(ChatRole.System, Instructions.Replace("{now}", _clock.GetUtcNow().ToString("yyyy-MM-dd HH:mm 'UTC'"))),
            .. _history,
            new(ChatRole.User, question)
        ];

        string answer;
        try
        {
            var response = await _model.CompleteAsync(messages, new ChatOptions { Tools = _tools, ToolMode = ChatToolMode.Auto }, cancellationToken);
            answer = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text))?.Text ?? "";
        }
        catch (ClientResultException ex) when (SafetyValidator.IsContentFilterBlock(ex))
        {
            var filtered = _safety.ContentFilterRefusal();
            Remember(question, filtered);
            return new AgentTurn(filtered, _ledger.Calls, true, ["Blocked by the Foundry DefaultV2 content filter."]);
        }

        var analysis = TryParse(answer) ?? await ReformatAsync(answer, cancellationToken);
        var structured = analysis is not null;
        analysis ??= Unstructured(answer);

        var earlierAnswers = _history.Where(m => m.Role == ChatRole.Assistant).Select(m => m.Text);
        var validated = _safety.ValidateOutput(analysis, _ledger.Sources, earlierAnswers);

        Remember(question, validated.Analysis);
        return new AgentTurn(validated.Analysis, _ledger.Calls, structured, validated.Notes);
    }

    private async Task<OpsAnalysis?> ReformatAsync(string answer, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(answer)) return null;

        var response = await _model.CompleteAsync(
            [new(ChatRole.System, ReformatInstructions), new(ChatRole.User, answer)],
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
            cancellationToken);

        return TryParse(response.Text);
    }

    private static OpsAnalysis? TryParse(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<OpsAnalysis>(text[start..(end + 1)], MockData.Json);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Summary)) return null;

            return parsed with
            {
                Severity = string.IsNullOrWhiteSpace(parsed.Severity) ? "Unknown" : parsed.Severity.Trim(),
                Evidence = parsed.Evidence ?? [],
                RecommendedActions = parsed.RecommendedActions ?? [],
                Sources = parsed.Sources ?? []
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static OpsAnalysis Unstructured(string answer) => new(
        Summary: string.IsNullOrWhiteSpace(answer) ? "The model returned no answer." : answer.Trim(),
        Severity: "Unknown",
        Evidence: [],
        RecommendedActions: ["The answer could not be parsed into a structured result; review it manually."],
        Sources: [],
        NeedsHumanEscalation: true);

    private void Remember(string question, OpsAnalysis analysis)
    {
        _history.Add(new ChatMessage(ChatRole.User, question));
        _history.Add(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(analysis, MockData.Json)));

        var excess = _history.Count - _maxHistoryMessages;
        if (excess > 0) _history.RemoveRange(0, excess);
    }
}
