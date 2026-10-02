using Microsoft.Extensions.Configuration;

namespace AgentOpsCopilot.Services;

/// <summary>Connection settings for the Microsoft Foundry deployment.</summary>
public sealed record FoundryOptions(Uri Endpoint, string Deployment, string ApiKey)
{
    /// <summary>
    /// Builds configuration from appsettings.json, user-secrets and environment variables
    /// (later sources win). Short env vars FOUNDRY_ENDPOINT / FOUNDRY_API_KEY / FOUNDRY_DEPLOYMENT
    /// are supported alongside the standard Foundry__Endpoint form.
    /// </summary>
    public static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets(typeof(FoundryOptions).Assembly, optional: true)
            .AddEnvironmentVariables()
            .AddInMemoryCollection(ShortEnvironmentVariables())
            .Build();

    /// <summary>Reads and validates Foundry settings; throws with setup instructions if anything is missing.</summary>
    public static FoundryOptions FromConfiguration(IConfiguration config)
    {
        var endpoint = config["Foundry:Endpoint"];
        var deployment = config["Foundry:Deployment"];
        var apiKey = config["Foundry:ApiKey"];

        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(endpoint))
            problems.Add("Foundry:Endpoint is not set.");
        else if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps)
            problems.Add("Foundry:Endpoint must be an absolute https:// URL.");
        if (string.IsNullOrWhiteSpace(deployment))
            problems.Add("Foundry:Deployment is not set.");
        if (string.IsNullOrWhiteSpace(apiKey))
            problems.Add("Foundry:ApiKey is not set.");

        if (problems.Count > 0)
            throw new InvalidOperationException(
                "Foundry configuration is incomplete:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(p => "  - " + p)) + Environment.NewLine +
                "Fix it with (from the AgentOpsCopilot project folder):" + Environment.NewLine +
                "  dotnet user-secrets set \"Foundry:Endpoint\" \"https://<resource>.services.ai.azure.com/openai/v1/\"" + Environment.NewLine +
                "  dotnet user-secrets set \"Foundry:ApiKey\" \"<your key>\"" + Environment.NewLine +
                "or set the FOUNDRY_ENDPOINT / FOUNDRY_API_KEY environment variables.");

        return new FoundryOptions(new Uri(endpoint!), deployment!, apiKey!);
    }

    /// <summary>Safe for logs: never includes the API key.</summary>
    public override string ToString() => $"{Deployment} @ {Endpoint.Host} (API key: set)";

    private static Dictionary<string, string?> ShortEnvironmentVariables()
    {
        var map = new Dictionary<string, string?>();
        void Map(string env, string key)
        {
            var value = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrWhiteSpace(value)) map[key] = value;
        }
        Map("FOUNDRY_ENDPOINT", "Foundry:Endpoint");
        Map("FOUNDRY_API_KEY", "Foundry:ApiKey");
        Map("FOUNDRY_DEPLOYMENT", "Foundry:Deployment");
        return map;
    }
}
