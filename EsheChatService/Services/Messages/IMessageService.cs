using EsheChatService.Models;

namespace EsheChatService.Services.Messages
{
    public interface IMessageService
    {
        Task PersistAsync(Guid sessionId, DateTime updatedAt, ChatMessage message);
        Task DeleteAsync(Guid messageId, Guid userId, IReadOnlyList<ChatSession> userSessions);
    }
}
