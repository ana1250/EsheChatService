using System.Security.Claims;
using EsheChatService.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    string? Email { get; }
}

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;
    private readonly IDbContextFactory<ChatDbContext> _dbFactory;
    private readonly ILogger<CurrentUser> _logger;

    private Guid? _cachedUserId;

    public CurrentUser(
        IHttpContextAccessor http,
        IDbContextFactory<ChatDbContext> dbFactory,
        ILogger<CurrentUser> logger)
    {
        _http = http;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public bool IsAuthenticated =>
        _http.HttpContext?.User?.Identity?.IsAuthenticated == true;

    public string? Email =>
        _http.HttpContext?.User?
            .FindFirst(ClaimTypes.Email)?.Value;

    public Guid UserId
    {
        get
        {
            if (!IsAuthenticated)
                return Guid.Empty;

            if (_cachedUserId.HasValue)
                return _cachedUserId.Value;

            using var db = _dbFactory.CreateDbContext();

            _cachedUserId = db.Users
                .Where(u => u.Email == Email && u.IsActive)
                .Select(u => u.Id)
                .FirstOrDefault();

            if (_cachedUserId == Guid.Empty)
            {
                _logger.LogWarning("Authenticated user not found in database: {Email}", Email);
            }

            return _cachedUserId.Value;
        }
    }

}
