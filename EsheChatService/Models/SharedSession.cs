namespace EsheChatService.Models
{
    public class SharedSession
    {
        public Guid Id { get; set; }
        public Guid ChatSessionId { get; set; }
        public ChatSession ChatSession { get; set; } = null!;

        public Guid SharedWithUserId{ get; set; }
        public string? SharedWithEmail { get; set; }

        public DateTime SharedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RemovedAt { get; set; }
    }
}
