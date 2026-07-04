using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using EsheChatService.Models;

namespace EsheChatService.Services.Chat.Strategies
{
    public class GeminiChatStrategy : IChatClientStrategy
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<GeminiChatStrategy> _logger;

        public AiProvider Provider => AiProvider.Gemini;
        public string DefaultModel => "gemini-1.5-pro";

        public GeminiChatStrategy(HttpClient http, IConfiguration config, ILogger<GeminiChatStrategy> logger)
        {
            _http = http;
            _config = config;
            _logger = logger;
        }

        public async IAsyncEnumerable<StreamToken> GetReplyStreamAsync(
            List<ChatMessage> preparedMessages,
            string? model = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var apiKey = _config["AI:Gemini:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("Gemini API key not configured");
                yield return new StreamToken("**Google Gemini API Key Not Configured:** Please configure `AI:Gemini:ApiKey` in `appsettings.json` or User Secrets.", null, DefaultModel);
                yield break;
            }

            var targetModel = !string.IsNullOrWhiteSpace(model) ? model : DefaultModel;
            var sessionId = preparedMessages[0].ChatSessionId;

            _logger.LogInformation("Gemini strategy streaming started for session {SessionId} with model {Model}",
                sessionId, targetModel);

            var messages = preparedMessages.Select(m => new
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
                model = targetModel,
                messages,
                temperature = 0.7,
                max_tokens = 1000,
                stream = true,
                stream_options = new { include_usage = true }
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json")
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

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
                    _logger.LogError("Gemini API error {StatusCode} for session {SessionId}: {ResponseBody}",
                        response.StatusCode, sessionId, errorBody);
                    throw new Exception($"Gemini error ({response.StatusCode}): {errorBody}");
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream);

                int totalChars = 0;
                string? modelName = targetModel;

                while (!reader.EndOfStream)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
                        continue;

                    var data = line.Substring(6);
                    if (data == "[DONE]")
                        break;

                    string? token = null;
                    TokenUsage? usage = null;

                    try
                    {
                        using var doc = JsonDocument.Parse(data);

                        if (doc.RootElement.TryGetProperty("model", out var modelEl))
                        {
                            modelName = modelEl.GetString() ?? targetModel;
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

                        if (doc.RootElement.TryGetProperty("usage", out var usageEl))
                        {
                            usage = new TokenUsage(
                                usageEl.GetProperty("prompt_tokens").GetInt32(),
                                usageEl.GetProperty("completion_tokens").GetInt32(),
                                usageEl.GetProperty("total_tokens").GetInt32());
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse Gemini SSE chunk for session {SessionId}: {Data}",
                            sessionId, data);
                        continue;
                    }

                    if (!string.IsNullOrEmpty(token))
                        totalChars += token.Length;

                    if (!string.IsNullOrEmpty(token) || usage != null)
                    {
                        yield return new StreamToken(token, usage, modelName);
                    }
                }

                _logger.LogInformation("Gemini streaming completed for session {SessionId} ({TotalChars} chars)",
                    sessionId, totalChars);
            }
            finally
            {
                response?.Dispose();
            }
        }
    }
}
