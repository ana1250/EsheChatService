using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EsheChatService.Models;

namespace EsheChatService.Services
{
    public class ChatService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ICurrentUser _currentUser;
        private readonly ILogger<ChatService> _logger;

        private static readonly string[] GuestReplies = new[]
        {
            "Hi! Please sign in to use the full features of Eshe Chat.",
            "Welcome! To get the best experience and save your chats, please sign in.",
            "Hello there! Most of my advanced features require a signed-in account. Please log in to continue.",
            "It looks like you're browsing as a guest. Please sign in to unlock my full potential!",
            "To keep this conversation going and access all tools, please take a moment to sign in."
        };

        public ChatService(HttpClient http, IConfiguration config, ICurrentUser currentUser, ILogger<ChatService> logger)
        {
            _http = http;
            _config = config;
            _currentUser = currentUser;
            _logger = logger;
        }

        public async Task<ChatReply> GetReplyAsync(List<ChatMessage> history, CancellationToken cancellationToken = default)
        {
            if (history == null || history.Count == 0)
                throw new ArgumentException("Conversation history is empty");

            if (!_currentUser.IsAuthenticated)
            {
                _logger.LogDebug("Guest user requested AI reply — returning sign-in prompt");
                var random = new Random();
                return new ChatReply(GuestReplies[random.Next(GuestReplies.Length)], null);
            }

            // Ensure valid messages only
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

            var apiKey = _config["AI:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("AI API key not configured");

            var sessionId = ordered[0].ChatSessionId;
            _logger.LogInformation("AI request started for session {SessionId} with {MessageCount} messages",
                sessionId, ordered.Count);

            var messages = ordered.Select(m => new
            {
                role = m.Role switch
                {
                    ChatRole.System => "system",
                    ChatRole.Assistant => "assistant",
                    _ => "user"
                },
                content = m.Content
            });

            var requestBody = new
            {
                model = "mistral-large-latest",
                messages,
                temperature = 0.7,
                max_tokens = 1000
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://api.mistral.ai/v1/chat/completions")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json")
            };

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            try
            {
                using var response = await _http.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Mistral API error {StatusCode} for session {SessionId}: {ResponseBody}",
                        response.StatusCode, sessionId, body);
                    throw new Exception($"Mistral error ({response.StatusCode}): {body}");
                }

                using var doc = JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
                    choices.GetArrayLength() == 0)
                {
                    _logger.LogError("Mistral API returned empty choices for session {SessionId}", sessionId);
                    throw new Exception("Mistral response missing choices");
                }

                var reply = choices[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString()
                    ?.Trim()
                    ?? "No response";

                // Extract token usage
                TokenUsage? usage = null;
                if (doc.RootElement.TryGetProperty("usage", out var usageEl))
                {
                    usage = new TokenUsage(
                        usageEl.GetProperty("prompt_tokens").GetInt32(),
                        usageEl.GetProperty("completion_tokens").GetInt32(),
                        usageEl.GetProperty("total_tokens").GetInt32());

                    _logger.LogInformation(
                        "Token usage for session {SessionId}: prompt={Prompt}, completion={Completion}, total={Total}",
                        sessionId, usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens);
                }

                _logger.LogInformation("AI response received for session {SessionId} ({ReplyLength} chars)",
                    sessionId, reply.Length);

                return new ChatReply(reply, usage);
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("AI request cancelled by user for session {SessionId}", sessionId);
                return new ChatReply("*(Generation stopped by user)*", null);
            }
        }

    }
}
