using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;

namespace AgentOpsCopilot.Services;

public sealed class FoundryChatService : IAgentService, IDisposable
{
    private const int MaxToolRoundTrips = 8;
    private const int MaxRetries = 5;

    private readonly IChatClient _client;

    public FoundryChatService(FoundryOptions options)
    {
        var openAi = new OpenAIClient(new ApiKeyCredential(options.ApiKey), new OpenAIClientOptions
        {
            Endpoint = options.Endpoint,
            RetryPolicy = new TransientNotFoundRetryPolicy(MaxRetries)
        });

        _client = new ChatClientBuilder(openAi.GetChatClient(options.Deployment).AsIChatClient())
            .UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = MaxToolRoundTrips)
            .Build();
    }

    public Task<ChatResponse> CompleteAsync(IList<ChatMessage> messages, ChatOptions options, CancellationToken cancellationToken = default) =>
        _client.GetResponseAsync(messages, options, cancellationToken);

    public void Dispose() => _client.Dispose();

    private sealed class TransientNotFoundRetryPolicy(int maxRetries) : ClientRetryPolicy(maxRetries)
    {
        protected override bool ShouldRetry(PipelineMessage message, Exception? exception) =>
            base.ShouldRetry(message, exception) || message.Response?.Status == 404;

        protected override async ValueTask<bool> ShouldRetryAsync(PipelineMessage message, Exception? exception) =>
            await base.ShouldRetryAsync(message, exception) || message.Response?.Status == 404;
    }
}
