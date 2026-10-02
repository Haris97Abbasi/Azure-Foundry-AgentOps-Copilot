using Microsoft.Extensions.AI;

namespace AgentOpsCopilot.Services;

public interface IAgentService
{
    Task<ChatResponse> CompleteAsync(IList<ChatMessage> messages, ChatOptions options, CancellationToken cancellationToken = default);
}
