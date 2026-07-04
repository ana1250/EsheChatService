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

    /// <summary>
    /// A single chunk from the streaming API. Contains either a text token,
    /// usage metadata from the final chunk, or both.
    /// </summary>
    public record StreamToken(string? Text, TokenUsage? Usage, string? Model);
}

