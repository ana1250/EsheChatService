using EsheChatService.Data;
using EsheChatService.Models;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EsheChatService.Tests;

public class UserManagerTests
{
    private IDbContextFactory<ChatDbContext> CreateInMemoryFactory(string dbName)
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var factoryMock = new Mock<IDbContextFactory<ChatDbContext>>();
        factoryMock.Setup(f => f.CreateDbContext())
            .Returns(() => new ChatDbContext(options));
        factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ChatDbContext(options));

        return factoryMock.Object;
    }

    [Fact]
    public async Task ValidateAndUpdateGoogleUserAsync_ExistingUser_UpdatesLastLogin()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = CreateInMemoryFactory(dbName);

        // Seed a user
        using (var db = factory.CreateDbContext())
        {
            db.Users.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                Email = "test@example.com",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var manager = new UserManager(factory);
        await manager.ValidateAndUpdateGoogleUserAsync("test@example.com", "google-sub-123");

        using (var db = factory.CreateDbContext())
        {
            var user = await db.Users.FirstAsync(u => u.Email == "test@example.com");
            Assert.NotNull(user.LastLoginAt);
            Assert.Equal("google-sub-123", user.ExternalUserId);
        }
    }

    [Fact]
    public async Task ValidateAndUpdateGoogleUserAsync_ExistingUserWithSub_DoesNotOverwrite()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = CreateInMemoryFactory(dbName);

        using (var db = factory.CreateDbContext())
        {
            db.Users.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                Email = "test@example.com",
                ExternalUserId = "original-sub",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var manager = new UserManager(factory);
        await manager.ValidateAndUpdateGoogleUserAsync("test@example.com", "new-sub-attempt");

        using (var db = factory.CreateDbContext())
        {
            var user = await db.Users.FirstAsync(u => u.Email == "test@example.com");
            Assert.Equal("original-sub", user.ExternalUserId);
        }
    }

    [Fact]
    public async Task ValidateAndUpdateGoogleUserAsync_UnknownEmail_Throws()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = CreateInMemoryFactory(dbName);

        var manager = new UserManager(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.ValidateAndUpdateGoogleUserAsync("nobody@example.com", "sub-123"));
    }
}
