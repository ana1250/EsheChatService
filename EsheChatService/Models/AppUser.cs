namespace EsheChatService.Models
{
    public class AppUser
    {
        public Guid Id { get; set; }

        public string Email { get; set; } = null!;

        // Google "sub" claim
        public string? ExternalUserId { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }

        public bool IsActive { get; set; }
    }
}
