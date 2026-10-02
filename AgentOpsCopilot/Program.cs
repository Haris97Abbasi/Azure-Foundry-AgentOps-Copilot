using AgentOpsCopilot.Services;

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
