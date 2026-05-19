using EsheChatService.Models;
using EsheChatService.Services.Repositories;
using Microsoft.Extensions.Logging;

namespace EsheChatService.Services.Messages
{
    public class MessageService : IMessageService
    {
        private readonly IChatRepository _repository;
        private readonly ILogger<MessageService> _logger;

        public MessageService(IChatRepository repository, ILogger<MessageService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task PersistAsync(Guid sessionId, DateTime updatedAt, ChatMessage message)
        {
            var exists = await _repository.MessageExistsAsync(message.Id);
            if (exists) return;

            await _repository.UpdateSessionTimeAndAddMessageAsync(sessionId, updatedAt, message);

            _logger.LogDebug("Message persisted: {MessageId} Role={Role} Session={SessionId}",
                message.Id, message.Role, sessionId);
        }

        public async Task DeleteAsync(Guid messageId, Guid userId, IReadOnlyList<ChatSession> userSessions)
        {
            var msg = await _repository.GetMessageAsync(messageId);
            if (msg == null) return;

            // Enforce ownership: the message's session must belong to the user
            if (!userSessions.Any(s => s.Id == msg.ChatSessionId && s.UserOwnerId == userId))
            {
                _logger.LogWarning("DeleteMessageAsync blocked: message {MessageId} not owned by {UserId}",
                    messageId, userId);
                return;
            }

            await _repository.DeleteMessageAsync(msg);

            _logger.LogInformation("Message deleted: {MessageId}", messageId);
        }
    }
}
