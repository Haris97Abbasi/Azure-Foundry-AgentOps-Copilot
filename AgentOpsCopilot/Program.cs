using System.Text;
using System.Text.Json;
using AgentOpsCopilot.Agents;
using AgentOpsCopilot.Guardrails;
using AgentOpsCopilot.Memory;
using AgentOpsCopilot.Services;
using AgentOpsCopilot.Tools;

Console.OutputEncoding = Encoding.UTF8;
var rawJson = args.Contains("--json", StringComparer.OrdinalIgnoreCase);

var configuration = FoundryOptions.BuildConfiguration();
FoundryOptions foundry;
try
{
    foundry = FoundryOptions.FromConfiguration(configuration);
}
catch (InvalidOperationException ex)
{
    WriteLine(ex.Message, ConsoleColor.Red);
    return 1;
}

var baseDirectory = AppContext.BaseDirectory;
var maxMessages = int.TryParse(configuration["Memory:MaxMessages"], out var configured) && configured > 0 ? configured : 10;

var ledger = new EvidenceLedger();
var data = new MockData(Path.Combine(baseDirectory, "Data"));
var memory = new JsonConversationStore(Path.Combine(baseDirectory, "Data", "session.json"), maxMessages);
using var model = new FoundryChatService(foundry);
var agent = new OpsCopilotAgent(
    model,
    ledger,
    new SafetyValidator(),
    memory,
    new IncidentTools(data, ledger),
    new ChangeTools(data, ledger),
    new RunbookSearchTool(Path.Combine(baseDirectory, "Knowledge"), ledger));

WriteLine("AgentOps Copilot", ConsoleColor.Cyan);
WriteLine($"Model: {foundry}", ConsoleColor.DarkGray);
if (memory.LoadWarning is not null) WriteLine(memory.LoadWarning, ConsoleColor.DarkYellow);
WriteLine(memory.Messages.Count == 0
    ? "Starting a new conversation."
    : $"Restored {memory.Messages.Count} messages from the previous session ({memory.Path}).", ConsoleColor.DarkGray);
WriteLine("Ask an operations question, or type /help.", ConsoleColor.DarkGray);

while (true)
{
    Console.WriteLine();
    Write("ops> ", ConsoleColor.Green);
    var input = Console.ReadLine();
    if (input is null) break;
    input = input.Trim();
    if (input.Length == 0) continue;

    if (input.Equals("/exit", StringComparison.OrdinalIgnoreCase) || input.Equals("/quit", StringComparison.OrdinalIgnoreCase)) break;
    if (input.Equals("/help", StringComparison.OrdinalIgnoreCase))
    {
        WriteLine("/reset  forget the conversation (clears Data/session.json)", ConsoleColor.DarkGray);
        WriteLine("/exit   quit (the conversation is kept for next time)", ConsoleColor.DarkGray);
        WriteLine("Start with --json to print raw OpsAnalysis JSON.", ConsoleColor.DarkGray);
        continue;
    }
    if (input.Equals("/reset", StringComparison.OrdinalIgnoreCase))
    {
        memory.Clear();
        WriteLine("Conversation memory cleared.", ConsoleColor.DarkGray);
        continue;
    }

    if (!Console.IsOutputRedirected) Write("Investigating...", ConsoleColor.DarkGray);
    AgentTurn turn;
    try
    {
        turn = await agent.AskAsync(input);
    }
    catch (Exception ex)
    {
        ClearStatus();
        var reason = ex is AggregateException retries
            ? $"{retries.InnerExceptions[0].Message} (after {retries.InnerExceptions.Count} attempts)"
            : ex.Message.Split('\n')[0];
        WriteLine($"The request failed: {reason}", ConsoleColor.Red);
        WriteLine("Nothing was saved to memory. Try again in a moment.", ConsoleColor.DarkGray);
        continue;
    }
    ClearStatus();

    Render(turn);
}

WriteLine("Goodbye. The conversation is saved for next time.", ConsoleColor.DarkGray);
return 0;

void Render(AgentTurn turn)
{
    if (turn.ToolCalls.Count > 0)
        WriteLine("Tools: " + string.Join("  |  ", turn.ToolCalls.Select(c =>
            $"{c.Tool}({c.Arguments}) -> {(c.ReturnedSources.Count == 0 ? "nothing" : string.Join(", ", c.ReturnedSources))}")), ConsoleColor.DarkGray);
    else
        WriteLine("Tools: none", ConsoleColor.DarkGray);

    if (rawJson)
    {
        Console.WriteLine(JsonSerializer.Serialize(turn.Analysis, new JsonSerializerOptions(MockData.Json) { WriteIndented = true }));
    }
    else
    {
        var a = turn.Analysis;
        Console.WriteLine();
        Heading("Summary");
        Console.WriteLine($"  {a.Summary}");

        Heading("Severity");
        WriteLine($"  {a.Severity}", a.Severity switch
        {
            "SEV1" => ConsoleColor.Red,
            "SEV2" => ConsoleColor.Yellow,
            "SEV3" => ConsoleColor.Cyan,
            "SEV4" => ConsoleColor.Green,
            _ => ConsoleColor.Gray
        });

        Heading("Evidence");
        if (a.Evidence.Count == 0) WriteLine("  (none)", ConsoleColor.DarkGray);
        foreach (var item in a.Evidence) Console.WriteLine($"  - {item}");

        Heading("Recommended actions");
        if (a.RecommendedActions.Count == 0) WriteLine("  (none)", ConsoleColor.DarkGray);
        for (var i = 0; i < a.RecommendedActions.Count; i++) Console.WriteLine($"  {i + 1}. {a.RecommendedActions[i]}");

        Heading("Sources");
        WriteLine(a.Sources.Count == 0 ? "  (none)" : "  " + string.Join(", ", a.Sources), a.Sources.Count == 0 ? ConsoleColor.DarkGray : ConsoleColor.Gray);

        Heading("Human escalation");
        WriteLine(a.NeedsHumanEscalation ? "  YES - involve the on-call engineer before acting" : "  No", a.NeedsHumanEscalation ? ConsoleColor.Red : ConsoleColor.Green);
    }

    if (!turn.Structured)
        WriteLine("Note: the model's answer could not be parsed into OpsAnalysis; showing it as-is.", ConsoleColor.DarkYellow);
    foreach (var note in turn.GuardrailNotes)
        WriteLine($"Guardrail: {note}", ConsoleColor.DarkYellow);
}

void Heading(string text) => WriteLine(text, ConsoleColor.Cyan);

void ClearStatus()
{
    if (Console.IsOutputRedirected) return;
    Console.Write("\r" + new string(' ', 20) + "\r");
}

static void Write(string text, ConsoleColor color)
{
    var previous = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.Write(text);
    Console.ForegroundColor = previous;
}

static void WriteLine(string text, ConsoleColor color) => Write(text + Environment.NewLine, color);
