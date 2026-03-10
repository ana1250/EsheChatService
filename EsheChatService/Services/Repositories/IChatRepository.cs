using EsheChatService.Models;

namespace EsheChatService.Services.Repositories
{
    public interface IChatRepository
    {
        Task<List<ChatFolder>> GetUserFoldersAsync(Guid userId);
        Task<List<ChatSession>> GetUserSessionsAsync(Guid userId);
        Task<List<ChatSession>> GetSharedSessionsAsync(Guid userId, string email);
        
        Task<ChatFolder> CreateFolderAsync(ChatFolder folder);
        Task UpdateFolderAsync(ChatFolder folder);
        Task DeleteFolderAsync(ChatFolder folder);
        Task DeleteFolderAndSessionsAsync(ChatFolder folder);
        
        Task<ChatSession> CreateSessionAsync(ChatSession session);
        Task UpdateSessionAsync(ChatSession session);
        Task DeleteSessionAsync(ChatSession session);
        
        Task<List<ChatMessage>> GetSessionMessagesAsync(Guid sessionId);
        Task<ChatMessage?> GetMessageAsync(Guid messageId);
        Task<bool> MessageExistsAsync(Guid messageId);
        Task AddMessageAsync(ChatMessage message);
        Task UpdateSessionTimeAndAddMessageAsync(Guid sessionId, DateTime updatedAt, ChatMessage message);
        Task DeleteMessageAsync(ChatMessage message);
        
        Task<SharedSession?> GetSharedSessionAsync(Guid sharedSessionId);
        Task<List<SharedSession>> GetSharedSessionsBySessionIdAsync(Guid sessionId);
        Task AddSharedSessionAsync(SharedSession sharedSession);
        Task UpdateSharedSessionAsync(SharedSession sharedSession);
        
        Task<Guid> GetUserIdByEmailAsync(string email);
    }
}
