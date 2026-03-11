using Microsoft.AspNetCore.SignalR;
using EsheChatService.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EsheChatService.Hubs
{
    public class ChatHub : Hub
    {
        private readonly IDbContextFactory<ChatDbContext> _dbFactory;
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(IDbContextFactory<ChatDbContext> dbFactory, ILogger<ChatHub> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        private async Task<bool> IsUserAuthorizedForSession(Guid sessionId)
        {
            var email = Context.User?.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(email))
            {
                _logger.LogDebug("Auth check failed: no email claim on connection {ConnectionId}. IsAuthenticated={IsAuth}",
                    Context.ConnectionId, Context.User?.Identity?.IsAuthenticated);
                return false;
            }

            using var db = await _dbFactory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
            if (user == null)
            {
                _logger.LogDebug("Auth check failed: user {Email} not found in DB", email);
                return false;
            }

            var session = await db.ChatSessions.FindAsync(sessionId);
            if (session == null)
            {
                _logger.LogDebug("Auth check failed: session {SessionId} not found", sessionId);
                return false;
            }

            bool isOwner = session.UserOwnerId == user.Id;
            bool isShared = await db.SharedSessions.AnyAsync(s => s.ChatSessionId == sessionId && (s.SharedWithUserId == user.Id || s.SharedWithEmail == email) && s.RemovedAt == null);

            if (!isOwner && !isShared)
            {
                _logger.LogDebug("Auth check failed: user {UserId} is neither owner nor shared for session {SessionId}",
                    user.Id, sessionId);
            }

            return isOwner || isShared;
        }

        public override async Task OnConnectedAsync()
        {
            var email = Context.User?.FindFirst(ClaimTypes.Email)?.Value;
            _logger.LogInformation("SignalR connected: {ConnectionId} (User: {Email})",
                Context.ConnectionId, email ?? "anonymous");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var email = Context.User?.FindFirst(ClaimTypes.Email)?.Value;
            if (exception != null)
            {
                _logger.LogWarning(exception, "SignalR disconnected with error: {ConnectionId} (User: {Email})",
                    Context.ConnectionId, email ?? "anonymous");
            }
            else
            {
                _logger.LogInformation("SignalR disconnected: {ConnectionId} (User: {Email})",
                    Context.ConnectionId, email ?? "anonymous");
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinSession(Guid sessionId)
        {
            if (await IsUserAuthorizedForSession(sessionId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, sessionId.ToString());
                _logger.LogInformation("Connection {ConnectionId} joined session {SessionId}",
                    Context.ConnectionId, sessionId);
            }
            else
            {
                _logger.LogWarning("Unauthorized JoinSession attempt: {ConnectionId} for session {SessionId}",
                    Context.ConnectionId, sessionId);
            }
        }

        public async Task LeaveSession(Guid sessionId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId.ToString());
            _logger.LogInformation("Connection {ConnectionId} left session {SessionId}",
                Context.ConnectionId, sessionId);
        }

        public async Task SendAssistantMessage(Guid sessionId, Guid messageId, string content)
        {
            if (!await IsUserAuthorizedForSession(sessionId))
            {
                _logger.LogWarning("Unauthorized SendAssistantMessage: {ConnectionId} for session {SessionId}",
                    Context.ConnectionId, sessionId);
                return;
            }

            await Clients
                .OthersInGroup(sessionId.ToString())
                .SendAsync("AssistantMessageReceived", messageId, content);

            _logger.LogDebug("Assistant message {MessageId} broadcast to session {SessionId}",
                messageId, sessionId);
        }

        public async Task SendUserMessage(Guid sessionId, Guid messageId, string content)
        {
            if (!await IsUserAuthorizedForSession(sessionId))
            {
                _logger.LogWarning("Unauthorized SendUserMessage: {ConnectionId} for session {SessionId}",
                    Context.ConnectionId, sessionId);
                return;
            }

            await Clients
                .OthersInGroup(sessionId.ToString())
                .SendAsync("UserMessageReceived", messageId, content);

            _logger.LogDebug("User message {MessageId} broadcast to session {SessionId}",
                messageId, sessionId);
        }

        public async Task AssistantTyping(Guid sessionId, bool isTyping)
        {
            if (!await IsUserAuthorizedForSession(sessionId))
            {
                _logger.LogWarning("Unauthorized AssistantTyping: {ConnectionId} for session {SessionId}",
                    Context.ConnectionId, sessionId);
                return;
            }

            await Clients
                .OthersInGroup(sessionId.ToString())
                .SendAsync("AssistantTyping", isTyping);
        }
    }
}
