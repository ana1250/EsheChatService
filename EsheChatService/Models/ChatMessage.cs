namespace EsheChatService.Models
{
    public enum ChatRole
    {
        User,
        Assistant,
        System
    }

    public class ChatMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public ChatRole Role { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Guid ChatSessionId { get; set; }

        public ChatMessage() { }
        public ChatMessage(ChatRole role, string content, Guid chatSessionId)
        {
            Role = role;
            Content = content;
            ChatSessionId = chatSessionId;
        }
    }

}
