using EsheChatService.Models;
using EsheChatService.Services;
using EsheChatService.Services.Repositories;
using Microsoft.Extensions.Logging;
using Moq;

namespace EsheChatService.Tests;

public class ChatSessionManagerTests
{
    private readonly Mock<IChatRepository> _repoMock;
    private readonly Mock<ICurrentUser> _userMock;
    private readonly ChatSessionManager _manager;
    private readonly Guid _userId = Guid.NewGuid();

    public ChatSessionManagerTests()
    {
        _repoMock = new Mock<IChatRepository>();
        _userMock = new Mock<ICurrentUser>();
        _userMock.Setup(u => u.UserId).Returns(_userId);
        _userMock.Setup(u => u.IsAuthenticated).Returns(true);
        _userMock.Setup(u => u.Email).Returns("test@example.com");

        _manager = new ChatSessionManager(
            _repoMock.Object,
            _userMock.Object,
            Mock.Of<ILogger<ChatSessionManager>>());
    }

    // ---- Load ----

    [Fact]
    public async Task LoadAsync_LoadsFoldersSessionsAndShared()
    {
        var folders = new List<ChatFolder> { new() { Id = Guid.NewGuid(), Name = "Work", UserOwnerId = _userId } };
        var sessions = new List<ChatSession> { new() { Id = Guid.NewGuid(), Title = "Chat 1", UserOwnerId = _userId } };
        var shared = new List<ChatSession> { new() { Id = Guid.NewGuid(), Title = "Shared Chat" } };

        _repoMock.Setup(r => r.GetUserFoldersAsync(_userId)).ReturnsAsync(folders);
        _repoMock.Setup(r => r.GetUserSessionsAsync(_userId)).ReturnsAsync(sessions);
        _repoMock.Setup(r => r.GetSharedSessionsAsync(_userId, "test@example.com")).ReturnsAsync(shared);

        await _manager.LoadAsync();

        Assert.Equal(2, _manager.Sessions.Count); // 1 owned + 1 shared
        Assert.Single(_manager.Folders);
        Assert.Single(_manager.SharedWithMe);
    }

    [Fact]
    public async Task LoadAsync_CalledTwice_OnlyLoadsOnce()
    {
        _repoMock.Setup(r => r.GetUserFoldersAsync(_userId)).ReturnsAsync(new List<ChatFolder>());
        _repoMock.Setup(r => r.GetUserSessionsAsync(_userId)).ReturnsAsync(new List<ChatSession>());
        _repoMock.Setup(r => r.GetSharedSessionsAsync(_userId, "test@example.com")).ReturnsAsync(new List<ChatSession>());

        await _manager.LoadAsync();
        await _manager.LoadAsync();

        _repoMock.Verify(r => r.GetUserFoldersAsync(_userId), Times.Once);
    }

    // ---- AddMessageInMemory ----

    [Fact]
    public async Task AddMessageInMemory_WithNoActiveSession_ReturnsNull()
    {
        var result = _manager.AddMessageInMemory(ChatRole.User, "Hello");

        Assert.Null(result);
    }

    [Fact]
    public async Task AddMessageInMemory_AddsMessageToActiveSession()
    {
        _repoMock.Setup(r => r.GetUserFoldersAsync(_userId)).ReturnsAsync(new List<ChatFolder>());
        _repoMock.Setup(r => r.GetUserSessionsAsync(_userId)).ReturnsAsync(new List<ChatSession>());
        _repoMock.Setup(r => r.GetSharedSessionsAsync(_userId, "test@example.com")).ReturnsAsync(new List<ChatSession>());
        _repoMock.Setup(r => r.CreateSessionAsync(It.IsAny<ChatSession>()))
            .ReturnsAsync((ChatSession s) => s);

        await _manager.LoadAsync();
        await _manager.CreateSessionAsync("First message");

        var msg = _manager.AddMessageInMemory(ChatRole.User, "Hello");

        Assert.NotNull(msg);
        Assert.Equal(ChatRole.User, msg!.Role);
        Assert.Equal("Hello", msg.Content);
        Assert.Single(_manager.ActiveSession!.Messages);
    }

    [Fact]
    public async Task AddMessageInMemory_WithCustomId_UsesProvidedId()
    {
        _repoMock.Setup(r => r.GetUserFoldersAsync(_userId)).ReturnsAsync(new List<ChatFolder>());
        _repoMock.Setup(r => r.GetUserSessionsAsync(_userId)).ReturnsAsync(new List<ChatSession>());
        _repoMock.Setup(r => r.GetSharedSessionsAsync(_userId, "test@example.com")).ReturnsAsync(new List<ChatSession>());
        _repoMock.Setup(r => r.CreateSessionAsync(It.IsAny<ChatSession>()))
            .ReturnsAsync((ChatSession s) => s);

        await _manager.LoadAsync();
        await _manager.CreateSessionAsync("Test");

        var customId = Guid.NewGuid();
        var msg = _manager.AddMessageInMemory(ChatRole.Assistant, "Reply", customId);

        Assert.Equal(customId, msg!.Id);
    }

    // ---- CreateSession ----

    [Fact]
    public async Task CreateSessionAsync_SetsActiveSession()
    {
        _repoMock.Setup(r => r.GetUserFoldersAsync(_userId)).ReturnsAsync(new List<ChatFolder>());
        _repoMock.Setup(r => r.GetUserSessionsAsync(_userId)).ReturnsAsync(new List<ChatSession>());
        _repoMock.Setup(r => r.GetSharedSessionsAsync(_userId, "test@example.com")).ReturnsAsync(new List<ChatSession>());
        _repoMock.Setup(r => r.CreateSessionAsync(It.IsAny<ChatSession>()))
            .ReturnsAsync((ChatSession s) => s);

        await _manager.LoadAsync();
        await _manager.CreateSessionAsync("Hello World");

        Assert.NotNull(_manager.ActiveSession);
        Assert.Equal("Hello World", _manager.ActiveSession!.Title);
        Assert.Equal(_userId, _manager.ActiveSession.UserOwnerId);
    }

    [Fact]
    public async Task CreateSessionAsync_TruncatesLongTitle()
    {
        _repoMock.Setup(r => r.GetUserFoldersAsync(_userId)).ReturnsAsync(new List<ChatFolder>());
        _repoMock.Setup(r => r.GetUserSessionsAsync(_userId)).ReturnsAsync(new List<ChatSession>());
        _repoMock.Setup(r => r.GetSharedSessionsAsync(_userId, "test@example.com")).ReturnsAsync(new List<ChatSession>());
        _repoMock.Setup(r => r.CreateSessionAsync(It.IsAny<ChatSession>()))
            .ReturnsAsync((ChatSession s) => s);

        await _manager.LoadAsync();
        await _manager.CreateSessionAsync("This is a very long message that should be truncated to forty characters and then ellipsis appended");

        Assert.True(_manager.ActiveSession!.Title.Length <= 44); // 40 chars + "..."
        Assert.EndsWith("...", _manager.ActiveSession.Title);
    }

    // ---- Rename ----

    [Fact]
    public async Task RenameSessionAsync_OwnerCanRename()
    {
        _repoMock.Setup(r => r.GetUserFoldersAsync(_userId)).ReturnsAsync(new List<ChatFolder>());
        var session = new ChatSession { Id = Guid.NewGuid(), Title = "Old", UserOwnerId = _userId };
        _repoMock.Setup(r => r.GetUserSessionsAsync(_userId)).ReturnsAsync(new List<ChatSession> { session });
        _repoMock.Setup(r => r.GetSharedSessionsAsync(_userId, "test@example.com")).ReturnsAsync(new List<ChatSession>());

        await _manager.LoadAsync();
        await _manager.RenameSessionAsync(session.Id, "New Title");

        Assert.Equal("New Title", session.Title);
        _repoMock.Verify(r => r.UpdateSessionAsync(It.IsAny<ChatSession>()), Times.Once);
    }

    [Fact]
    public async Task RenameSessionAsync_NonOwner_DoesNothing()
    {
        _repoMock.Setup(r => r.GetUserFoldersAsync(_userId)).ReturnsAsync(new List<ChatFolder>());
        var otherUserId = Guid.NewGuid();
        var session = new ChatSession { Id = Guid.NewGuid(), Title = "Old", UserOwnerId = otherUserId };
        _repoMock.Setup(r => r.GetUserSessionsAsync(_userId)).ReturnsAsync(new List<ChatSession> { session });
        _repoMock.Setup(r => r.GetSharedSessionsAsync(_userId, "test@example.com")).ReturnsAsync(new List<ChatSession>());

        await _manager.LoadAsync();
        await _manager.RenameSessionAsync(session.Id, "Hacked");

        Assert.Equal("Old", session.Title);
        _repoMock.Verify(r => r.UpdateSessionAsync(It.IsAny<ChatSession>()), Times.Never);
    }

    // ---- Delete ----

    [Fact]
    public async Task DeleteSessionAsync_OwnerCanDelete()
    {
        _repoMock.Setup(r => r.GetUserFoldersAsync(_userId)).ReturnsAsync(new List<ChatFolder>());
        var session = new ChatSession { Id = Guid.NewGuid(), Title = "Delete me", UserOwnerId = _userId };
        _repoMock.Setup(r => r.GetUserSessionsAsync(_userId)).ReturnsAsync(new List<ChatSession> { session });
        _repoMock.Setup(r => r.GetSharedSessionsAsync(_userId, "test@example.com")).ReturnsAsync(new List<ChatSession>());

        await _manager.LoadAsync();
        await _manager.DeleteSessionAsync(session.Id);

        Assert.Empty(_manager.Sessions);
        _repoMock.Verify(r => r.DeleteSessionAsync(session), Times.Once);
    }

    [Fact]
    public async Task DeleteSessionAsync_NonOwner_DoesNothing()
    {
        _repoMock.Setup(r => r.GetUserFoldersAsync(_userId)).ReturnsAsync(new List<ChatFolder>());
        var session = new ChatSession { Id = Guid.NewGuid(), Title = "Not mine", UserOwnerId = Guid.NewGuid() };
        _repoMock.Setup(r => r.GetUserSessionsAsync(_userId)).ReturnsAsync(new List<ChatSession> { session });
        _repoMock.Setup(r => r.GetSharedSessionsAsync(_userId, "test@example.com")).ReturnsAsync(new List<ChatSession>());

        await _manager.LoadAsync();
        await _manager.DeleteSessionAsync(session.Id);

        Assert.Single(_manager.Sessions);
        _repoMock.Verify(r => r.DeleteSessionAsync(It.IsAny<ChatSession>()), Times.Never);
    }

    [Fact]
    public async Task DeleteSessionAsync_ClearsActiveIfDeleted()
    {
        _repoMock.Setup(r => r.GetUserFoldersAsync(_userId)).ReturnsAsync(new List<ChatFolder>());
        var session = new ChatSession { Id = Guid.NewGuid(), Title = "Active", UserOwnerId = _userId };
        _repoMock.Setup(r => r.GetUserSessionsAsync(_userId)).ReturnsAsync(new List<ChatSession> { session });
        _repoMock.Setup(r => r.GetSharedSessionsAsync(_userId, "test@example.com")).ReturnsAsync(new List<ChatSession>());
        _repoMock.Setup(r => r.GetSessionMessagesAsync(session.Id)).ReturnsAsync(new List<ChatMessage>());

        await _manager.LoadAsync();
        await _manager.SetActiveAsync(session.Id);
        Assert.NotNull(_manager.ActiveSession);

        await _manager.DeleteSessionAsync(session.Id);
        Assert.Null(_manager.ActiveSession);
    }

    // ---- Folders ----

    [Fact]
    public async Task CreateFolderAsync_AddsFolderToMemoryAndDB()
    {
        _repoMock.Setup(r => r.GetUserFoldersAsync(_userId)).ReturnsAsync(new List<ChatFolder>());
        _repoMock.Setup(r => r.GetUserSessionsAsync(_userId)).ReturnsAsync(new List<ChatSession>());
        _repoMock.Setup(r => r.GetSharedSessionsAsync(_userId, "test@example.com")).ReturnsAsync(new List<ChatSession>());
        _repoMock.Setup(r => r.CreateFolderAsync(It.IsAny<ChatFolder>()))
            .ReturnsAsync((ChatFolder f) => f);

        await _manager.LoadAsync();
        var folder = await _manager.CreateFolderAsync("Projects");

        Assert.Single(_manager.Folders);
        Assert.Equal("Projects", folder.Name);
        Assert.Equal(_userId, folder.UserOwnerId);
    }

    [Fact]
    public async Task DeleteFolderAsync_NonOwner_DoesNothing()
    {
        var folder = new ChatFolder { Id = Guid.NewGuid(), Name = "Not Mine", UserOwnerId = Guid.NewGuid() };
        _repoMock.Setup(r => r.GetUserFoldersAsync(_userId)).ReturnsAsync(new List<ChatFolder> { folder });
        _repoMock.Setup(r => r.GetUserSessionsAsync(_userId)).ReturnsAsync(new List<ChatSession>());
        _repoMock.Setup(r => r.GetSharedSessionsAsync(_userId, "test@example.com")).ReturnsAsync(new List<ChatSession>());

        await _manager.LoadAsync();
        await _manager.DeleteFolderAsync(folder.Id);

        Assert.Single(_manager.Folders);
        _repoMock.Verify(r => r.DeleteFolderAsync(It.IsAny<ChatFolder>()), Times.Never);
    }

    // ---- Move ----

    [Fact]
    public async Task MoveSessionToFolderAsync_OwnerCanMove()
    {
        var folder = new ChatFolder { Id = Guid.NewGuid(), Name = "Work", UserOwnerId = _userId };
        var session = new ChatSession { Id = Guid.NewGuid(), Title = "Movable", UserOwnerId = _userId, FolderId = null };

        _repoMock.Setup(r => r.GetUserFoldersAsync(_userId)).ReturnsAsync(new List<ChatFolder> { folder });
        _repoMock.Setup(r => r.GetUserSessionsAsync(_userId)).ReturnsAsync(new List<ChatSession> { session });
        _repoMock.Setup(r => r.GetSharedSessionsAsync(_userId, "test@example.com")).ReturnsAsync(new List<ChatSession>());

        await _manager.LoadAsync();
        await _manager.MoveSessionToFolderAsync(session.Id, folder.Id);

        Assert.Equal(folder.Id, session.FolderId);
        _repoMock.Verify(r => r.UpdateSessionAsync(session), Times.Once);
    }

    // ---- OnChange ----

    [Fact]
    public async Task OnChange_FiresOnCreateSession()
    {
        _repoMock.Setup(r => r.GetUserFoldersAsync(_userId)).ReturnsAsync(new List<ChatFolder>());
        _repoMock.Setup(r => r.GetUserSessionsAsync(_userId)).ReturnsAsync(new List<ChatSession>());
        _repoMock.Setup(r => r.GetSharedSessionsAsync(_userId, "test@example.com")).ReturnsAsync(new List<ChatSession>());
        _repoMock.Setup(r => r.CreateSessionAsync(It.IsAny<ChatSession>()))
            .ReturnsAsync((ChatSession s) => s);

        await _manager.LoadAsync();

        bool eventFired = false;
        _manager.OnChange += () => eventFired = true;

        await _manager.CreateSessionAsync("Trigger event");

        Assert.True(eventFired);
    }

    // ---- ClearActive ----

    [Fact]
    public void ClearActive_SetsActiveSessionToNull()
    {
        _manager.ClearActive();
        Assert.Null(_manager.ActiveSession);
    }
}
