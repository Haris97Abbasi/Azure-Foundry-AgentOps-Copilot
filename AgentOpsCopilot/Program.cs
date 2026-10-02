using AgentOpsCopilot.Services;

// Temporary entry point for step 4: verifies configuration loads. Replaced by the console host in step 13.
try
{
    var foundry = FoundryOptions.FromConfiguration(FoundryOptions.BuildConfiguration());
    Console.WriteLine($"Configuration OK: {foundry}");
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
return 0;
