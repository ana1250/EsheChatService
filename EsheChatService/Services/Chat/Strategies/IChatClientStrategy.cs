using EsheChatService.Models;

namespace EsheChatService.Services.Chat.Strategies
{
    public interface IChatClientStrategy
    {
        AiProvider Provider { get; }
        string DefaultModel { get; }
        IAsyncEnumerable<StreamToken> GetReplyStreamAsync(
            List<ChatMessage> preparedMessages,
            string? model = null,
            CancellationToken cancellationToken = default);
    }
}
