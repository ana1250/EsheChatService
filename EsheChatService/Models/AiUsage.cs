namespace EsheChatService.Models
{
    public enum AiProvider
    {
        Mistral,
        OpenAI,
        Gemini,
        Anthropic
    }

    /// <summary>
    /// Tracks AI token usage and model metadata for a single assistant message.
    /// 1:1 relationship with ChatMessage (only assistant messages get a row).
    /// </summary>
    public class AiUsage
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ChatMessageId { get; set; }
        public ChatMessage ChatMessage { get; set; } = null!;

        public AiProvider Provider { get; set; } = AiProvider.Mistral;

        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }

        /// <summary>
        /// The model identifier used for this request (e.g. "mistral-large-latest").
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// The specific model version returned by the API, if available.
        /// </summary>
        public string? ModelVersion { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
