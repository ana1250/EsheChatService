using EsheChatService.Models;
using EsheChatService.Services.Repositories;
using Microsoft.Extensions.Logging;

namespace EsheChatService.Services.Sharing
{
    public class ShareService : IShareService
    {
        private readonly IChatRepository _repository;
        private readonly ILogger<ShareService> _logger;

        public ShareService(IChatRepository repository, ILogger<ShareService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task ShareAsync(ChatSession session, string email)
        {
            var targetUserId = await _repository.GetUserIdByEmailAsync(email);

            var sharedSession = new SharedSession
            {
                SharedWithUserId = targetUserId,
                ChatSessionId = session.Id,
                SharedWithEmail = email,
                SharedAt = DateTime.UtcNow
            };

            await _repository.AddSharedSessionAsync(sharedSession);

            _logger.LogInformation("Session {SessionId} shared with {Email} by User={UserId}",
                session.Id, email, session.UserOwnerId);
        }

        public async Task RemoveShareAsync(Guid sharedSessionId, Guid userId)
        {
            var sharedSession = await _repository.GetSharedSessionAsync(sharedSessionId);
            if (sharedSession == null || sharedSession.ChatSession.UserOwnerId != userId)
                return;

            sharedSession.RemovedAt = DateTime.UtcNow;
            await _repository.UpdateSharedSessionAsync(sharedSession);

            _logger.LogInformation("Share removed: {SharedSessionId} Session={SessionId} by User={UserId}",
                sharedSessionId, sharedSession.ChatSessionId, userId);
        }

        public async Task<List<SharedSession>> GetSharesBySessionAsync(Guid sessionId)
        {
            return await _repository.GetSharedSessionsBySessionIdAsync(sessionId);
        }
    }
}
