namespace EsheChatService.Models
{
    public record AiModelOption(
        AiProvider Provider,
        string ModelId,
        string DisplayName,
        string Description,
        bool IsDefault = false
    );
}
