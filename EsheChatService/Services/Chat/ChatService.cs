using System.Runtime.CompilerServices;
using System.Text;
using EsheChatService.Models;
using EsheChatService.Services.Chat.Strategies;
using EsheChatService.Services.User;

namespace EsheChatService.Services
{
    public class ChatService
    {
        private readonly IChatStrategyFactory _strategyFactory;
        private readonly ICurrentUser _currentUser;
        private readonly ILogger<ChatService> _logger;

        private static readonly string[] GuestReplies = new[]
        {
            "Hi! Please sign in to use the full features of Eshe Chat.",
            "Welcome! To get the best experience and save your chats, please sign in.",
            "Hello there! Most of my advanced features require a signed-in account. Please sign in to continue.",
            "It looks like you're browsing as a guest. Please sign in to unlock my full potential!",
            "To keep this conversation going and access all tools, please take a moment to sign in."
        };

        public ChatService(IChatStrategyFactory strategyFactory, ICurrentUser currentUser, ILogger<ChatService> logger)
        {
            _strategyFactory = strategyFactory;
            _currentUser = currentUser;
            _logger = logger;
        }

        /// <summary>
        /// Prepares and validates the message history for an API request.
        /// Returns null if the user is a guest (caller should handle guest replies separately).
        /// </summary>
        private List<ChatMessage>? PrepareMessages(List<ChatMessage> history)
        {
            if (history == null || history.Count == 0)
                throw new ArgumentException("Conversation history is empty");

            var ordered = history
                .Where(m =>
                    !string.IsNullOrWhiteSpace(m.Content) &&
                    (m.Role == ChatRole.User || m.Role == ChatRole.Assistant || m.Role == ChatRole.System))
                .OrderBy(m => m.CreatedAt)
                .TakeLast(10)
                .ToList();

            if (!ordered.Any(m => m.Role == ChatRole.User))
                throw new InvalidOperationException("Conversation must contain at least one user message");

            if (ordered[0].Role != ChatRole.System)
            {
                ordered.Insert(0, new ChatMessage(
                    ChatRole.System,
                    "You are a helpful, concise assistant.",
                    ordered[0].ChatSessionId
                ));
            }

            return ordered;
        }

        /// <summary>
        /// Streams AI response tokens using the resolved provider strategy (Mistral, OpenAI, Gemini).
        /// For guest users, yields a single sign-in prompt string.
        /// </summary>
        public async IAsyncEnumerable<StreamToken> GetReplyStreamAsync(
            List<ChatMessage> history,
            AiProvider provider = AiProvider.Mistral,
            string? model = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!_currentUser.IsAuthenticated)
            {
                _logger.LogDebug("Guest user requested AI reply — returning sign-in prompt");
                var random = new Random();
                yield return new StreamToken(GuestReplies[random.Next(GuestReplies.Length)], null, null);
                yield break;
            }

            var ordered = PrepareMessages(history);
            if (ordered == null)
                yield break;

            var strategy = _strategyFactory.GetStrategy(provider);

            await foreach (var token in strategy.GetReplyStreamAsync(ordered, model, cancellationToken))
            {
                yield return token;
            }
        }

        public async Task<ChatReply> GetReplyAsync(
            List<ChatMessage> history,
            AiProvider provider = AiProvider.Mistral,
            string? model = null,
            CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();
            TokenUsage? usage = null;

            await foreach (var token in GetReplyStreamAsync(history, provider, model, cancellationToken))
            {
                if (!string.IsNullOrEmpty(token.Text))
                {
                    sb.Append(token.Text);
                }
                if (token.Usage != null)
                {
                    usage = token.Usage;
                }
            }

            return new ChatReply(sb.ToString(), usage);
        }
    }
}
