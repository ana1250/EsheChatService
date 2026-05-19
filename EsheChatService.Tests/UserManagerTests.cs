using EsheChatService.Models;
using EsheChatService.Services.Repositories;
using EsheChatService.Services.User;
using Microsoft.Extensions.Logging;
using Moq;

namespace EsheChatService.Tests;

public class UserManagerTests
{
    private readonly Mock<IChatRepository> _repoMock;
    private readonly UserManager _manager;

    public UserManagerTests()
    {
        _repoMock = new Mock<IChatRepository>();
        _manager = new UserManager(_repoMock.Object, Mock.Of<ILogger<UserManager>>());
    }

    [Fact]
    public async Task ValidateAndUpdateGoogleUserAsync_ExistingUser_UpdatesLastLogin()
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _repoMock.Setup(r => r.GetUserByEmailAsync("test@example.com"))
            .ReturnsAsync(user);

        await _manager.ValidateAndUpdateGoogleUserAsync("test@example.com", "google-sub-123");

        Assert.NotNull(user.LastLoginAt);
        Assert.Equal("google-sub-123", user.ExternalUserId);
        _repoMock.Verify(r => r.UpdateUserAsync(user), Times.Once);
    }

    [Fact]
    public async Task ValidateAndUpdateGoogleUserAsync_ExistingUserWithSub_DoesNotOverwrite()
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            ExternalUserId = "original-sub",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _repoMock.Setup(r => r.GetUserByEmailAsync("test@example.com"))
            .ReturnsAsync(user);

        await _manager.ValidateAndUpdateGoogleUserAsync("test@example.com", "new-sub-attempt");

        Assert.Equal("original-sub", user.ExternalUserId);
        _repoMock.Verify(r => r.UpdateUserAsync(user), Times.Once);
    }

    [Fact]
    public async Task ValidateAndUpdateGoogleUserAsync_UnknownEmail_Throws()
    {
        _repoMock.Setup(r => r.GetUserByEmailAsync("nobody@example.com"))
            .ReturnsAsync((AppUser?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.ValidateAndUpdateGoogleUserAsync("nobody@example.com", "sub-123"));
    }
}
