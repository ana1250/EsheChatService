using EsheChatService.Data;
using EsheChatService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EsheChatService.Services.Repositories
{
    public class ChatRepository : IChatRepository
    {
        private readonly IDbContextFactory<ChatDbContext> _dbFactory;
        private readonly ILogger<ChatRepository> _logger;

        public ChatRepository(IDbContextFactory<ChatDbContext> dbFactory, ILogger<ChatRepository> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        public async Task<List<ChatFolder>> GetUserFoldersAsync(Guid userId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.ChatFolders
                .Where(f => f.UserOwnerId == userId)
                .OrderBy(f => f.Name)
                .ToListAsync();
        }

        public async Task<List<ChatSession>> GetUserSessionsAsync(Guid userId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.ChatSessions
                .Where(s => s.UserOwnerId == userId)
                .Include(s => s.Messages)
                .OrderByDescending(s => s.UpdatedAt)
                .ToListAsync();
        }

        public async Task<List<ChatSession>> GetSharedSessionsAsync(Guid userId, string email)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.SharedSessions
                .Where(sh =>
                    (sh.SharedWithUserId == userId || sh.SharedWithEmail == email) &&
                    sh.RemovedAt == null
                )
                .Include(sh => sh.ChatSession)
                    .ThenInclude(s => s.Messages)
                .OrderByDescending(sh => sh.ChatSession.UpdatedAt)
                .Select(sh => sh.ChatSession)
                .ToListAsync();
        }

        public async Task<ChatFolder> CreateFolderAsync(ChatFolder folder)
        {
            using var db = _dbFactory.CreateDbContext();
            db.ChatFolders.Add(folder);
            await db.SaveChangesAsync();
            _logger.LogInformation("Folder created: {FolderId} Name={FolderName} Owner={OwnerId}",
                folder.Id, folder.Name, folder.UserOwnerId);
            return folder;
        }

        public async Task UpdateFolderAsync(ChatFolder folder)
        {
            using var db = _dbFactory.CreateDbContext();
            db.ChatFolders.Update(folder);
            await db.SaveChangesAsync();
            _logger.LogDebug("Folder updated: {FolderId} Name={FolderName}", folder.Id, folder.Name);
        }

        public async Task DeleteFolderAsync(ChatFolder folder)
        {
            using var db = _dbFactory.CreateDbContext();
            db.ChatFolders.Remove(folder);
            await db.SaveChangesAsync();
            _logger.LogInformation("Folder deleted: {FolderId}", folder.Id);
        }

        public async Task DeleteFolderAndSessionsAsync(ChatFolder folder)
        {
            using var db = _dbFactory.CreateDbContext();
            var sessions = await db.ChatSessions
                .Where(s => s.FolderId == folder.Id)
                .ToListAsync();

            db.ChatSessions.RemoveRange(sessions);
            db.ChatFolders.Remove(folder);
            await db.SaveChangesAsync();
            _logger.LogInformation("Folder deleted with {SessionCount} sessions: {FolderId}",
                sessions.Count, folder.Id);
        }

        public async Task<ChatSession> CreateSessionAsync(ChatSession session)
        {
            using var db = _dbFactory.CreateDbContext();
            db.ChatSessions.Add(session);
            await db.SaveChangesAsync();
            _logger.LogInformation("Session created: {SessionId} Title={Title} Owner={OwnerId}",
                session.Id, session.Title, session.UserOwnerId);
            return session;
        }

        public async Task UpdateSessionAsync(ChatSession session)
        {
            using var db = _dbFactory.CreateDbContext();
            db.ChatSessions.Update(session);
            await db.SaveChangesAsync();
            _logger.LogDebug("Session updated: {SessionId} Title={Title}", session.Id, session.Title);
        }

        public async Task DeleteSessionAsync(ChatSession session)
        {
            using var db = _dbFactory.CreateDbContext();
            db.ChatSessions.Remove(session);
            await db.SaveChangesAsync();
            _logger.LogInformation("Session deleted: {SessionId}", session.Id);
        }

        public async Task<List<ChatMessage>> GetSessionMessagesAsync(Guid sessionId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.ChatMessages
                .Where(m => m.ChatSessionId == sessionId)
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Role)
                .ToListAsync();
        }

        public async Task<ChatMessage?> GetMessageAsync(Guid messageId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.ChatMessages.FindAsync(messageId);
        }

        public async Task<bool> MessageExistsAsync(Guid messageId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.ChatMessages.AnyAsync(m => m.Id == messageId);
        }

        public async Task AddMessageAsync(ChatMessage message)
        {
            using var db = _dbFactory.CreateDbContext();
            db.ChatMessages.Add(message);
            await db.SaveChangesAsync();
            _logger.LogDebug("Message persisted: {MessageId} Role={Role} Session={SessionId}",
                message.Id, message.Role, message.ChatSessionId);
        }

        public async Task UpdateSessionTimeAndAddMessageAsync(Guid sessionId, DateTime updatedAt, ChatMessage message)
        {
            using var db = _dbFactory.CreateDbContext();
            var session = await db.ChatSessions.FindAsync(sessionId);
            if (session != null)
            {
                session.UpdatedAt = updatedAt;
            }
            db.ChatMessages.Add(message);
            await db.SaveChangesAsync();
            _logger.LogDebug("Message persisted with session update: {MessageId} Role={Role} Session={SessionId}",
                message.Id, message.Role, sessionId);
        }

        public async Task DeleteMessageAsync(ChatMessage message)
        {
            using var db = _dbFactory.CreateDbContext();
            db.ChatMessages.Remove(message);
            await db.SaveChangesAsync();
            _logger.LogInformation("Message deleted: {MessageId}", message.Id);
        }

        public async Task<SharedSession?> GetSharedSessionAsync(Guid sharedSessionId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.SharedSessions
                .Include(s => s.ChatSession)
                .FirstOrDefaultAsync(s => s.Id == sharedSessionId);
        }

        public async Task<List<SharedSession>> GetSharedSessionsBySessionIdAsync(Guid sessionId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.SharedSessions
                .Where(sh => sh.ChatSessionId == sessionId && sh.RemovedAt == null)
                .ToListAsync();
        }

        public async Task AddSharedSessionAsync(SharedSession sharedSession)
        {
            using var db = _dbFactory.CreateDbContext();
            db.SharedSessions.Add(sharedSession);
            await db.SaveChangesAsync();
            _logger.LogInformation("Session {SessionId} shared with {Email}",
                sharedSession.ChatSessionId, sharedSession.SharedWithEmail);
        }

        public async Task UpdateSharedSessionAsync(SharedSession sharedSession)
        {
            using var db = _dbFactory.CreateDbContext();
            db.SharedSessions.Update(sharedSession);
            await db.SaveChangesAsync();
            _logger.LogDebug("SharedSession updated: {SharedSessionId} RemovedAt={RemovedAt}",
                sharedSession.Id, sharedSession.RemovedAt);
        }

        public async Task<Guid> GetUserIdByEmailAsync(string email)
        {
            using var db = _dbFactory.CreateDbContext();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                _logger.LogWarning("Share target user not found: {Email}", email);
            }
            return user?.Id ?? Guid.Empty;
        }
    }
}
