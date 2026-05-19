using EsheChatService.Models;

namespace EsheChatService.Services.Sessions
{
    public interface ISessionService
    {
        Task<ChatSession> CreateAsync(string firstUserMessage, Guid userId);
        Task RenameAsync(ChatSession session, string newTitle);
        Task DeleteAsync(ChatSession session);
        Task<List<ChatMessage>> GetMessagesAsync(Guid sessionId);
    }
}
