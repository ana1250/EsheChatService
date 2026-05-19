using EsheChatService.Models;

namespace EsheChatService.Services.Sharing
{
    public interface IShareService
    {
        Task ShareAsync(ChatSession session, string email);
        Task RemoveShareAsync(Guid sharedSessionId, Guid userId);
        Task<List<SharedSession>> GetSharesBySessionAsync(Guid sessionId);
    }
}
