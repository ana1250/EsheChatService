using EsheChatService.Models;

public class ChatFolder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int Order { get; set; }
    public bool IsExpanded { get; set; } = true;
    public Guid UserOwnerId { get; set; }

    public List<ChatSession> Sessions { get; set; } = new();
}
