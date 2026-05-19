using EsheChatService.Models;
using EsheChatService.Services.Repositories;
using Microsoft.Extensions.Logging;

namespace EsheChatService.Services.Sessions
{
    public class SessionService : ISessionService
    {
        private readonly IChatRepository _repository;
        private readonly ILogger<SessionService> _logger;

        public SessionService(IChatRepository repository, ILogger<SessionService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task<ChatSession> CreateAsync(string firstUserMessage, Guid userId)
        {
            var session = new ChatSession
            {
                Title = GenerateTitle(firstUserMessage),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                FolderId = null,
                UserOwnerId = userId
            };

            await _repository.CreateSessionAsync(session);

            _logger.LogInformation("Chat session created: {SessionId} Title={Title} User={UserId}",
                session.Id, session.Title, userId);

            return session;
        }

        public async Task RenameAsync(ChatSession session, string newTitle)
        {
            session.Title = newTitle.Trim();
            session.UpdatedAt = DateTime.UtcNow;

            await _repository.UpdateSessionAsync(session);

            _logger.LogInformation("Session renamed: {SessionId} NewTitle={Title}",
                session.Id, session.Title);
        }

        public async Task DeleteAsync(ChatSession session)
        {
            await _repository.DeleteSessionAsync(session);

            _logger.LogInformation("Chat session deleted: {SessionId}", session.Id);
        }

        public async Task<List<ChatMessage>> GetMessagesAsync(Guid sessionId)
        {
            return await _repository.GetSessionMessagesAsync(sessionId);
        }

        private static string GenerateTitle(string firstMessage)
        {
            if (string.IsNullOrWhiteSpace(firstMessage))
                return "New Chat";

            var cleaned = firstMessage.Trim().Replace("\r", "").Replace("\n", " ");
            if (cleaned.Length > 40)
                cleaned = cleaned[..40].Trim() + "...";

            return char.ToUpper(cleaned[0]) + cleaned[1..];
        }
    }
}
