using EsheChatService.Models;
using EsheChatService.Services.Repositories;
using Microsoft.Extensions.Logging;

namespace EsheChatService.Services.User
{
    public interface IUserManager
    {
        Task ValidateAndUpdateGoogleUserAsync(string email, string sub);
    }

    public class UserManager : IUserManager
    {
        private readonly IChatRepository _repository;
        private readonly ILogger<UserManager> _logger;

        public UserManager(IChatRepository repository, ILogger<UserManager> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task ValidateAndUpdateGoogleUserAsync(string email, string sub)
        {
            var user = await _repository.GetUserByEmailAsync(email);

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

            await _repository.UpdateUserAsync(user);
            _logger.LogInformation("User login successful: {UserId} ({Email})", user.Id, email);
        }
    }
}
