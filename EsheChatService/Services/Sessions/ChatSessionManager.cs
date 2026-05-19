using EsheChatService.Models;
using EsheChatService.Services.Folders;
using EsheChatService.Services.Messages;
using EsheChatService.Services.Repositories;
using EsheChatService.Services.Sessions;
using EsheChatService.Services.Sharing;
using EsheChatService.Services.User;
using Microsoft.Extensions.Logging;

namespace EsheChatService.Services
{
    public class ChatSessionManager
    {
        private readonly IChatRepository _repository;
        private readonly ISessionService _sessionService;
        private readonly IFolderService _folderService;
        private readonly IMessageService _messageService;
        private readonly IShareService _shareService;
        private readonly ICurrentUser _currentUser;
        private readonly ILogger<ChatSessionManager> _logger;

        private readonly List<ChatSession> _sessions = new();
        private readonly List<ChatFolder> _folders = new();
        private readonly List<ChatSession> _sharedWithMe = new();
        private readonly List<SharedSession> _sharedByMe = new();

        private bool _loaded;
        private readonly SemaphoreSlim _loadLock = new(1, 1);

        public IReadOnlyList<ChatSession> Sessions => _sessions;
        public IReadOnlyList<ChatFolder> Folders => _folders;
        public IReadOnlyList<ChatSession> SharedWithMe => _sharedWithMe;
        public IReadOnlyList<SharedSession> SharedByMe => _sharedByMe;

        public ChatSession? ActiveSession { get; private set; }
        public event Action? OnChange;
        private void NotifyStateChanged() => OnChange?.Invoke();

        public ChatSessionManager(
            IChatRepository repository,
            ISessionService sessionService,
            IFolderService folderService,
            IMessageService messageService,
            IShareService shareService,
            ICurrentUser currentUser,
            ILogger<ChatSessionManager> logger)
        {
            _repository = repository;
            _sessionService = sessionService;
            _folderService = folderService;
            _messageService = messageService;
            _shareService = shareService;
            _currentUser = currentUser;
            _logger = logger;
        }

        /* =============================================
           Load
        ============================================= */

        public async Task LoadAsync()
        {
            if (_loaded)
                return;

            await _loadLock.WaitAsync();
            try
            {
                if (_loaded)
                    return;

                _sessions.Clear();
                _folders.Clear();
                _sharedWithMe.Clear();

                var userId = _currentUser.UserId;

                _folders.AddRange(await _repository.GetUserFoldersAsync(userId));
                _sessions.AddRange(await _repository.GetUserSessionsAsync(userId));

                var email = _currentUser.Email;
                var sharedSessions = await _repository.GetSharedSessionsAsync(userId, email ?? "");
                _sharedWithMe.AddRange(sharedSessions);
                _sessions.AddRange(_sharedWithMe);

                _loaded = true;
                _logger.LogInformation(
                    "User data loaded: {UserId} ({SessionCount} sessions, {FolderCount} folders, {SharedCount} shared)",
                    userId, _sessions.Count, _folders.Count, _sharedWithMe.Count);
                NotifyStateChanged();
            }
            finally
            {
                _loadLock.Release();
            }
        }

        /* =============================================
           Sessions
        ============================================= */

        public async Task CreateSessionAsync(string firstUserMessage)
        {
            var session = await _sessionService.CreateAsync(firstUserMessage, _currentUser.UserId);

            _sessions.Insert(0, session);
            ActiveSession = session;
            NotifyStateChanged();
        }

        public async Task SetActiveAsync(Guid sessionId)
        {
            var existingSession = _sessions.FirstOrDefault(s => s.Id == sessionId);
            if (existingSession == null) return;

            existingSession.Messages = await _sessionService.GetMessagesAsync(sessionId);
            ActiveSession = existingSession;
            NotifyStateChanged();
        }

        public void ClearActive()
        {
            ActiveSession = null;
            NotifyStateChanged();
        }

        public async Task RenameSessionAsync(Guid sessionId, string newTitle)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == sessionId && s.UserOwnerId == _currentUser.UserId);
            if (session == null)
            {
                _logger.LogWarning("RenameSessionAsync blocked: session {SessionId} not owned by {UserId}",
                    sessionId, _currentUser.UserId);
                return;
            }

            await _sessionService.RenameAsync(session, newTitle);
            NotifyStateChanged();
        }

        public async Task DeleteSessionAsync(Guid sessionId)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == sessionId && s.UserOwnerId == _currentUser.UserId);
            if (session == null) return;

            _sessions.Remove(session);

            if (ActiveSession?.Id == sessionId)
                ActiveSession = null;

            await _sessionService.DeleteAsync(session);
            NotifyStateChanged();
        }

        /* =============================================
           Folders
        ============================================= */

        public IEnumerable<ChatSession> GetSessionsInFolder(Guid? folderId)
            => _sessions.Where(s => s.FolderId == folderId);

        public void ToggleFolder(Guid folderId)
        {
            var folder = _folders.FirstOrDefault(f => f.Id == folderId);
            if (folder == null) return;

            folder.IsExpanded = !folder.IsExpanded;
            NotifyStateChanged();
        }

        public async Task<ChatFolder> CreateFolderAsync(string name = "New Folder")
        {
            var folder = await _folderService.CreateAsync(name, _currentUser.UserId);

            _folders.Add(folder);
            NotifyStateChanged();

            return folder;
        }

        public async Task RenameFolderAsync(Guid folderId, string name)
        {
            var folder = _folders.FirstOrDefault(f => f.Id == folderId && f.UserOwnerId == _currentUser.UserId);
            if (folder == null)
            {
                _logger.LogWarning("RenameFolderAsync blocked: folder {FolderId} not owned by {UserId}",
                    folderId, _currentUser.UserId);
                return;
            }

            await _folderService.RenameAsync(folder, name);
            NotifyStateChanged();
        }

        public async Task DeleteFolderAsync(Guid folderId)
        {
            var folder = _folders.FirstOrDefault(f => f.Id == folderId && f.UserOwnerId == _currentUser.UserId);
            if (folder == null) return;

            // Unassign chats in memory
            foreach (var chat in _sessions.Where(s => s.FolderId == folderId))
                chat.FolderId = null;

            await _folderService.DeleteAsync(folder);

            _folders.Remove(folder);
            NotifyStateChanged();
        }

        public async Task DeleteFolderAndSessionsAsync(Guid folderId)
        {
            var folder = _folders.FirstOrDefault(f => f.Id == folderId && f.UserOwnerId == _currentUser.UserId);
            if (folder == null) return;

            await _folderService.DeleteWithSessionsAsync(folder);

            // Sync memory
            _sessions.RemoveAll(s => s.FolderId == folderId);
            _folders.RemoveAll(f => f.Id == folderId);

            if (ActiveSession != null && ActiveSession.FolderId == folderId)
                ActiveSession = null;

            NotifyStateChanged();
        }

        public async Task MoveSessionToFolderAsync(Guid sessionId, Guid? folderId)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == sessionId && s.UserOwnerId == _currentUser.UserId);
            if (session == null) return;

            await _folderService.MoveSessionToFolderAsync(session, folderId);
            NotifyStateChanged();
        }

        /* =============================================
           Messages (in-memory stays here)
        ============================================= */

        public ChatMessage? AddMessageInMemory(ChatRole role, string content, Guid? messageId = null)
        {
            if (ActiveSession == null) return null;

            var msg = new ChatMessage(role, content, ActiveSession.Id);
            if (messageId.HasValue)
                msg.Id = messageId.Value;

            ActiveSession.Messages.Add(msg);
            _logger.LogDebug("Message added in-memory: {MessageId} Role={Role} Session={SessionId}",
                msg.Id, role, ActiveSession.Id);
            return msg;
        }

        public async Task DeleteMessageAsync(Guid messageId)
        {
            await _messageService.DeleteAsync(messageId, _currentUser.UserId, _sessions);
        }

        public async Task PersistLastMessageAsync()
        {
            if (ActiveSession == null || !ActiveSession.Messages.Any())
                return;

            var message = ActiveSession.Messages.Last();
            ActiveSession.UpdatedAt = DateTime.UtcNow;

            await _messageService.PersistAsync(ActiveSession.Id, ActiveSession.UpdatedAt, message);
            NotifyStateChanged();
        }

        /* =============================================
           Sharing
        ============================================= */

        public async Task ShareSessionAsync(Guid chatSessionId, string email)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == chatSessionId && s.UserOwnerId == _currentUser.UserId);
            if (session == null) return;

            await _shareService.ShareAsync(session, email);
            NotifyStateChanged();
        }

        public async Task RemoveShareAsync(Guid sharedSessionId)
        {
            await _shareService.RemoveShareAsync(sharedSessionId, _currentUser.UserId);
            NotifyStateChanged();
        }

        public async Task GetSharedSessionbyIdAsync(Guid sessionId)
        {
            _sharedByMe.Clear();
            _sharedByMe.AddRange(await _shareService.GetSharesBySessionAsync(sessionId));
            NotifyStateChanged();
        }
    }
}
