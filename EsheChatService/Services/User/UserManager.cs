using EsheChatService.Data;
using Microsoft.EntityFrameworkCore;

public class UserManager
{
    private readonly IDbContextFactory<ChatDbContext> _dbFactory;

    public UserManager(IDbContextFactory<ChatDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task ValidateAndUpdateGoogleUserAsync(string email, string sub)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
            throw new InvalidOperationException("User is not registered");

        if (user.ExternalUserId == null)
            user.ExternalUserId = sub;

        user.LastLoginAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }
}
