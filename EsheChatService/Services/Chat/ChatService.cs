using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using EsheChatService.Models;
using EsheChatService.Services.User;

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
            "Hello there! Most of my advanced features require a signed-in account. Please sign in to continue.",
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
        /// Streams AI response tokens as they arrive from Mistral's SSE endpoint.
        /// Each yielded string is a small token/chunk of the response.
        /// For guest users, yields a single sign-in prompt string.
        /// </summary>
        public async IAsyncEnumerable<StreamToken> GetReplyStreamAsync(
            List<ChatMessage> history,
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

            var apiKey = _config["AI:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("AI API key not configured");

            var sessionId = ordered[0].ChatSessionId;
            _logger.LogInformation("AI streaming request started for session {SessionId} with {MessageCount} messages",
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
                max_tokens = 1000,
                stream = true,
                stream_options = new { include_usage = true }
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

            HttpResponseMessage? response = null;
            try
            {
                response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Mistral streaming API error {StatusCode} for session {SessionId}: {ResponseBody}",
                        response.StatusCode, sessionId, errorBody);
                    throw new Exception($"Mistral error ({response.StatusCode}): {errorBody}");
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream);

                int totalChars = 0;
                string? modelName = null;

                while (!reader.EndOfStream)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var line = await reader.ReadLineAsync(cancellationToken);

                    if (string.IsNullOrEmpty(line))
                        continue;

                    // SSE lines are prefixed with "data: "
                    if (!line.StartsWith("data: "))
                        continue;

                    var data = line.Substring(6); // Remove "data: " prefix

                    // "[DONE]" signals the end of the stream
                    if (data == "[DONE]")
                        break;

                    // Parse the JSON chunk
                    string? token = null;
                    TokenUsage? usage = null;
                    try
                    {
                        using var doc = JsonDocument.Parse(data);

                        // Capture model name from the first chunk that has it
                        if (modelName == null && doc.RootElement.TryGetProperty("model", out var modelEl))
                        {
                            modelName = modelEl.GetString();
                        }

                        if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                            choices.GetArrayLength() > 0)
                        {
                            var delta = choices[0].GetProperty("delta");
                            if (delta.TryGetProperty("content", out var contentEl))
                            {
                                token = contentEl.GetString();
                            }
                        }

                        // Usage arrives in the final chunk (when stream_options.include_usage = true)
                        if (doc.RootElement.TryGetProperty("usage", out var usageEl))
                        {
                            usage = new TokenUsage(
                                usageEl.GetProperty("prompt_tokens").GetInt32(),
                                usageEl.GetProperty("completion_tokens").GetInt32(),
                                usageEl.GetProperty("total_tokens").GetInt32());

                            _logger.LogInformation(
                                "Streaming token usage for session {SessionId}: prompt={Prompt}, completion={Completion}, total={Total}",
                                sessionId, usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse SSE chunk for session {SessionId}: {Data}",
                            sessionId, data);
                        continue;
                    }

                    if (!string.IsNullOrEmpty(token))
                    {
                        totalChars += token.Length;
                    }

                    // Yield if we have a text token or usage data (or both)
                    if (!string.IsNullOrEmpty(token) || usage != null)
                    {
                        yield return new StreamToken(token, usage, modelName);
                    }
                }

                _logger.LogInformation("AI streaming response completed for session {SessionId} ({TotalChars} chars)",
                    sessionId, totalChars);
            }
            finally
            {
                response?.Dispose();
            }
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
            var ordered = PrepareMessages(history)!;

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
