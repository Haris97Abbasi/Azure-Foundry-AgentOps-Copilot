using System.ComponentModel;
using System.Text.Json;

namespace AgentOpsCopilot.Tools;

public sealed class ChangeTools(MockData data, EvidenceLedger ledger)
{
    private const int MaxResults = 5;

    [Description("Lists the most recent deployments and configuration changes for a service, newest first. " +
                 "Use it to check whether a change lines up with when an incident started.")]
    public string GetRecentChanges(
        [Description("Service name, e.g. 'Checkout API'.")]
        string serviceName)
    {
        var changes = data.Changes
            .Where(c => MockData.ServiceMatches(c.Service, serviceName))
            .OrderByDescending(c => c.DeployedAt)
            .Take(MaxResults)
            .ToList();

        ledger.Record(nameof(GetRecentChanges), serviceName, changes.Select(c => c.Id));

        return JsonSerializer.Serialize(new
        {
            changes,
            note = changes.Count == 0 ? $"No recent changes found for '{serviceName}'." : null
        }, MockData.Json);
    }
}
