namespace EsheChatService.Models
{
    public class ChatSession
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = "New Chat";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public List<ChatMessage> Messages { get; set; } = new();

        public Guid? FolderId { get; set; }
        public ChatFolder? Folder { get; set; }
        public Guid? UserOwnerId { get; set; }
        public List<SharedSession> SharedWith { get; set; } = new();
    }

}
