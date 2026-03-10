using Microsoft.AspNetCore.SignalR;
using EsheChatService.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EsheChatService.Hubs
{
    public class ChatHub : Hub
    {
        private readonly IDbContextFactory<ChatDbContext> _dbFactory;

        public ChatHub(IDbContextFactory<ChatDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        private async Task<bool> IsUserAuthorizedForSession(Guid sessionId)
        {
            var email = Context.User?.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(email)) return false;

            using var db = await _dbFactory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
            if (user == null) return false;

            var session = await db.ChatSessions.FindAsync(sessionId);
            if (session == null) return false;

            bool isOwner = session.UserOwnerId == user.Id;
            bool isShared = await db.SharedSessions.AnyAsync(s => s.ChatSessionId == sessionId && (s.SharedWithUserId == user.Id || s.SharedWithEmail == email) && s.RemovedAt == null);

            return isOwner || isShared;
        }

        public async Task JoinSession(Guid sessionId)
        {
            if (await IsUserAuthorizedForSession(sessionId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, sessionId.ToString());
            }
        }

        public async Task LeaveSession(Guid sessionId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId.ToString());
        }

        public async Task SendAssistantMessage(Guid sessionId, Guid messageId, string content)
        {
            if (!await IsUserAuthorizedForSession(sessionId)) return;

            await Clients
                .OthersInGroup(sessionId.ToString())
                .SendAsync("AssistantMessageReceived", messageId, content);
        }

        public async Task SendUserMessage(Guid sessionId, Guid messageId, string content)
        {
            if (!await IsUserAuthorizedForSession(sessionId)) return;

            await Clients
                .OthersInGroup(sessionId.ToString())
                .SendAsync("UserMessageReceived", messageId, content);
        }

        public async Task AssistantTyping(Guid sessionId, bool isTyping)
        {
            if (!await IsUserAuthorizedForSession(sessionId)) return;

            await Clients
                .OthersInGroup(sessionId.ToString())
                .SendAsync("AssistantTyping", isTyping);
        }
    }
}
