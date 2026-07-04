using EsheChatService.Models;

namespace EsheChatService.Services.Chat.Strategies
{
    public interface IChatStrategyFactory
    {
        IChatClientStrategy GetStrategy(AiProvider provider = AiProvider.Mistral);
        IReadOnlyList<AiModelOption> GetAvailableModels();
    }

    public class ChatStrategyFactory : IChatStrategyFactory
    {
        private readonly IEnumerable<IChatClientStrategy> _strategies;

        private static readonly List<AiModelOption> AvailableModels = new()
        {
            new(AiProvider.Mistral, "mistral-large-latest", "Mistral Large", "Top-tier reasoning & multilingual model", IsDefault: true),
            new(AiProvider.Mistral, "mistral-medium-latest", "Mistral Medium", "Balanced performance and speed"),
            new(AiProvider.Mistral, "mistral-small-latest", "Mistral Small", "Fast & lightweight model"),
            new(AiProvider.OpenAI, "gpt-4o", "OpenAI GPT-4o", "Flagship multimodal intelligence model"),
            new(AiProvider.OpenAI, "gpt-4o-mini", "OpenAI GPT-4o Mini", "Fast and affordable small model"),
            new(AiProvider.Gemini, "gemini-1.5-pro", "Google Gemini 1.5 Pro", "High-reasoning 1M context window model"),
            new(AiProvider.Gemini, "gemini-1.5-flash", "Google Gemini 1.5 Flash", "Lightweight, fast multimodal model")
        };

        public ChatStrategyFactory(IEnumerable<IChatClientStrategy> strategies)
        {
            _strategies = strategies;
        }

        public IChatClientStrategy GetStrategy(AiProvider provider = AiProvider.Mistral)
        {
            var strategy = _strategies.FirstOrDefault(s => s.Provider == provider);
            if (strategy == null)
            {
                throw new NotSupportedException($"No strategy registered for provider {provider}");
            }
            return strategy;
        }

        public IReadOnlyList<AiModelOption> GetAvailableModels() => AvailableModels;
    }
}
