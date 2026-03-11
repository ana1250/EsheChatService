using EsheChatService.Data;
using Microsoft.EntityFrameworkCore;

public class UserManager
{
    private readonly IDbContextFactory<ChatDbContext> _dbFactory;
    private readonly ILogger<UserManager> _logger;

    public UserManager(IDbContextFactory<ChatDbContext> dbFactory, ILogger<UserManager> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task ValidateAndUpdateGoogleUserAsync(string email, string sub)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            _logger.LogWarning("Login rejected: unregistered email {Email}", email);
            throw new InvalidOperationException("User is not registered");
        }

        if (user.ExternalUserId == null)
        {
            user.ExternalUserId = sub;
            _logger.LogInformation("Google sub linked for user {UserId} ({Email})", user.Id, email);
        }

        user.LastLoginAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        _logger.LogInformation("User login successful: {UserId} ({Email})", user.Id, email);
    }
}
