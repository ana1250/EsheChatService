using EsheChatService.Models;
using EsheChatService.Services.Repositories;
using System.Security.Claims;

namespace EsheChatService.Services
{
    public class ChatSessionManager
    {
        private readonly IChatRepository _repository;
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
        private readonly ICurrentUser _currentUser;

        public ChatSessionManager(
            IChatRepository repository,
            ICurrentUser currentUser)
        {
            _repository = repository;
            _currentUser = currentUser;
        }

        /* ---------------- Load ---------------- */

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

                // folders (owned)
                _folders.AddRange(await _repository.GetUserFoldersAsync(userId));

                // owned sessions
                _sessions.AddRange(await _repository.GetUserSessionsAsync(userId));

                // shared sessions
                var email = _currentUser.Email;
                var sharedSessions = await _repository.GetSharedSessionsAsync(userId, email ?? "");

                _sharedWithMe.AddRange(sharedSessions);
                _sessions.AddRange(_sharedWithMe);

                _loaded = true;
                NotifyStateChanged();
            }
            finally
            {
                _loadLock.Release();
            }
        }

        /* ---------------- Folder Helpers---------------- */

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
            var folder = new ChatFolder { Name = name, UserOwnerId = _currentUser.UserId  };

            await _repository.CreateFolderAsync(folder);

            _folders.Add(folder);
            NotifyStateChanged();

            return folder;
        }

        public async Task RenameFolderAsync(Guid folderId, string name)
        {
            var folder = _folders.FirstOrDefault(f => f.Id == folderId && f.UserOwnerId == _currentUser.UserId);
            if (folder == null) return;

            folder.Name = name.Trim();

            await _repository.UpdateFolderAsync(folder);

            NotifyStateChanged();
        }

        public async Task DeleteFolderAsync(Guid folderId)
        {
            var folder = _folders.FirstOrDefault(f => f.Id == folderId && f.UserOwnerId == _currentUser.UserId);
            if (folder == null) return;

            // Unassign chats in memory
            foreach (var chat in _sessions.Where(s => s.FolderId == folderId))
                chat.FolderId = null;

            await _repository.DeleteFolderAsync(folder);

            _folders.Remove(folder);
            NotifyStateChanged();
        }

        public async Task MoveSessionToFolderAsync(Guid sessionId, Guid? folderId)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == sessionId && s.UserOwnerId == _currentUser.UserId);
            if (session == null) return;

            session.FolderId = folderId;

            await _repository.UpdateSessionAsync(session);

            NotifyStateChanged();
        }

        /* ---------------- Create ---------------- */

        public async Task CreateSessionAsync(string firstUserMessage)
        {
            var session = new ChatSession
            {
                Title = GenerateTitle(firstUserMessage),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                FolderId = null,
                UserOwnerId = _currentUser.UserId
            };

            await _repository.CreateSessionAsync(session);

            _sessions.Insert(0, session);
            ActiveSession = session;

            NotifyStateChanged();
        }

        /* ---------------- Active Session ---------------- */

        public async Task SetActiveAsync(Guid sessionId)
        {
            var existingSession = _sessions.FirstOrDefault(s => s.Id == sessionId);
            if (existingSession == null)
            {
                return;
            }

            existingSession.Messages = await _repository.GetSessionMessagesAsync(sessionId);

            ActiveSession = existingSession;
            NotifyStateChanged();
        }

        public void ClearActive()
        {
            ActiveSession = null;
            NotifyStateChanged();
        }

        /* ---------------- Messages ---------------- */

        public ChatMessage? AddMessageInMemory(ChatRole role, string content, Guid? messageId = null)
        {
            if (ActiveSession == null) return null;

            var msg = new ChatMessage(role, content, ActiveSession.Id);
            if (messageId.HasValue)
                msg.Id = messageId.Value;

            ActiveSession.Messages.Add(msg);
            return msg;
        }

        public async Task DeleteMessageAsync(Guid messageId)
        {
            var msg = await _repository.GetMessageAsync(messageId);
            if (msg == null) return;

            // Enforce ownership checks
            if (!_sessions.Any(s => s.Id == msg.ChatSessionId && s.UserOwnerId == _currentUser.UserId))
                return;

            await _repository.DeleteMessageAsync(msg);
        }

        public async Task PersistLastMessageAsync()
        {
            if (ActiveSession == null || !ActiveSession.Messages.Any())
                return;

            var message = ActiveSession.Messages.Last();

            var exists = await _repository.MessageExistsAsync(message.Id);
            if (exists) return;

            ActiveSession.UpdatedAt = DateTime.UtcNow;

            await _repository.UpdateSessionTimeAndAddMessageAsync(ActiveSession.Id, ActiveSession.UpdatedAt, message);

            NotifyStateChanged();
        }

        /* ---------------- Rename / Delete ---------------- */

        public async Task RenameSessionAsync(Guid sessionId, string newTitle)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == sessionId && s.UserOwnerId == _currentUser.UserId);
            if (session == null) return;

            session.Title = newTitle.Trim();
            session.UpdatedAt = DateTime.UtcNow;

            await _repository.UpdateSessionAsync(session);

            NotifyStateChanged();
        }

        public async Task DeleteSessionAsync(Guid sessionId)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == sessionId && s.UserOwnerId == _currentUser.UserId);
            if (session == null) return;

            _sessions.Remove(session);

            if (ActiveSession?.Id == sessionId)
                ActiveSession = null;

            await _repository.DeleteSessionAsync(session);

            NotifyStateChanged();
        }

        public async Task DeleteFolderAndSessionsAsync(Guid folderId)
        {
            var folder = _folders.FirstOrDefault(f => f.Id == folderId && f.UserOwnerId == _currentUser.UserId);
            if (folder == null) return;

            await _repository.DeleteFolderAndSessionsAsync(folder);

            // sync memory
            _sessions.RemoveAll(s => s.FolderId == folderId);
            _folders.RemoveAll(f => f.Id == folderId);

            if (ActiveSession != null && ActiveSession.FolderId == folderId)
                ActiveSession = null;

            NotifyStateChanged();
        }

        /* ---------------- Helpers ---------------- */

        private static string GenerateTitle(string firstMessage)
        {
            if (string.IsNullOrWhiteSpace(firstMessage))
                return "New Chat";

            var cleaned = firstMessage.Trim().Replace("\r", "").Replace("\n", " ");
            if (cleaned.Length > 40)
                cleaned = cleaned[..40].Trim() + "...";

            return char.ToUpper(cleaned[0]) + cleaned[1..];
        }

        /* -----------------Shared Sessions --------------*/
        public async Task RemoveShareAsync(Guid sharedSessionId)
        {
            var sharedSession = await _repository.GetSharedSessionAsync(sharedSessionId);
            if (sharedSession == null || sharedSession.ChatSession.UserOwnerId != _currentUser.UserId) return;

            sharedSession.RemovedAt = DateTime.UtcNow;
            await _repository.UpdateSharedSessionAsync(sharedSession);
            NotifyStateChanged();
        }

        public async Task GetSharedSessionbyIdAsync(Guid sessionId)
        {
            _sharedByMe.Clear();
            _sharedByMe.AddRange(await _repository.GetSharedSessionsBySessionIdAsync(sessionId));
            NotifyStateChanged();
        }

        public async Task ShareSessionAsync(Guid chatSessionId, string email)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == chatSessionId && s.UserOwnerId == _currentUser.UserId);
            if (session == null) return;

            var targetUserId = await _repository.GetUserIdByEmailAsync(email);

            var sharedSession = new SharedSession
            {
                SharedWithUserId = targetUserId,
                ChatSessionId = chatSessionId,
                SharedWithEmail = email,
                SharedAt = DateTime.UtcNow
            };

            await _repository.AddSharedSessionAsync(sharedSession);
            NotifyStateChanged();
        }
    }
}
