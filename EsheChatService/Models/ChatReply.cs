namespace EsheChatService.Models
{
    /// <summary>
    /// Wraps an AI response together with its token-usage metadata.
    /// </summary>
    public record ChatReply(string Content, TokenUsage? Usage);

    /// <summary>
    /// Token counts returned by the Mistral API.
    /// </summary>
    public record TokenUsage(int PromptTokens, int CompletionTokens, int TotalTokens);
}
