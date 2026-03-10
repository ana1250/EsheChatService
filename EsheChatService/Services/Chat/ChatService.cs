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

        private static readonly string[] GuestReplies = new[]
        {
            "Hi! Please sign in to use the full features of Eshe Chat.",
            "Welcome! To get the best experience and save your chats, please sign in.",
            "Hello there! Most of my advanced features require a signed-in account. Please log in to continue.",
            "It looks like you're browsing as a guest. Please sign in to unlock my full potential!",
            "To keep this conversation going and access all tools, please take a moment to sign in."
        };

        public ChatService(HttpClient http, IConfiguration config, ICurrentUser currentUser)
        {
            _http = http;
            _config = config;
            _currentUser = currentUser;
        }
        public async Task<string> GetReplyAsync(List<ChatMessage> history, CancellationToken cancellationToken = default)
        {
            if (history == null || history.Count == 0)
                throw new ArgumentException("Conversation history is empty");

            if (!_currentUser.IsAuthenticated)
            {
                var random = new Random();
                return GuestReplies[random.Next(GuestReplies.Length)];
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
                    throw new Exception($"Mistral error ({response.StatusCode}): {body}");

                using var doc = JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
                    choices.GetArrayLength() == 0)
                {
                    throw new Exception("Mistral response missing choices");
                }

                return choices[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString()
                    ?.Trim()
                    ?? "No response";
            }
            catch (TaskCanceledException)
            {
                return "*(Generation stopped by user)*";
            }
        }

    }
}
