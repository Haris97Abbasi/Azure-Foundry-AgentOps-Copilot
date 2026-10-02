using System.ComponentModel;
using System.Text.Json;

namespace AgentOpsCopilot.Tools;

public sealed class IncidentTools(MockData data, EvidenceLedger ledger)
{
    [Description("Lists active (unresolved) incidents with ID, service, severity, symptoms and start time. " +
                 "Call this before diagnosing any incident-specific question.")]
    public string GetActiveIncidents(
        [Description("Service to filter by, e.g. 'Checkout API'. Omit to list active incidents for all services.")]
        string? serviceName = null)
    {
        var incidents = data.Incidents
            .Where(i => i.Status.Equals("Active", StringComparison.OrdinalIgnoreCase) && MockData.ServiceMatches(i.Service, serviceName))
            .OrderByDescending(i => i.StartTime)
            .ToList();

        ledger.Record(nameof(GetActiveIncidents), serviceName ?? "(all)", incidents.Select(i => i.Id));

        return JsonSerializer.Serialize(new
        {
            incidents,
            note = incidents.Count == 0 ? $"No active incidents found for '{serviceName ?? "any service"}'." : null
        }, MockData.Json);
    }
}
