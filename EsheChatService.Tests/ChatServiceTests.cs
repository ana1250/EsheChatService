using System.Net;
using System.Text;
using System.Text.Json;
using EsheChatService.Models;
using EsheChatService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace EsheChatService.Tests;

public class ChatServiceTests
{
    private readonly Mock<ICurrentUser> _userMock;
    private readonly IConfiguration _config;

    public ChatServiceTests()
    {
        _userMock = new Mock<ICurrentUser>();

        var configData = new Dictionary<string, string?>
        {
            { "AI:ApiKey", "test-api-key" }
        };
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    private ChatService CreateServiceWithMockHttp(HttpResponseMessage response)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var httpClient = new HttpClient(handlerMock.Object);
        return new ChatService(httpClient, _config, _userMock.Object, Mock.Of<ILogger<ChatService>>());
    }

    // ---- Guest Mode ----

    [Fact]
    public async Task GetReplyAsync_GuestUser_ReturnsSignInPrompt()
    {
        _userMock.Setup(u => u.IsAuthenticated).Returns(false);

        var service = CreateServiceWithMockHttp(new HttpResponseMessage());
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello", Guid.NewGuid())
        };

        var reply = await service.GetReplyAsync(messages);

        Assert.Contains("sign in", reply.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetReplyAsync_GuestUser_DoesNotCallApi()
    {
        _userMock.Setup(u => u.IsAuthenticated).Returns(false);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage());

        var httpClient = new HttpClient(handlerMock.Object);
        var service = new ChatService(httpClient, _config, _userMock.Object, Mock.Of<ILogger<ChatService>>());

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello", Guid.NewGuid())
        };

        await service.GetReplyAsync(messages);

        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    // ---- Input Validation ----

    [Fact]
    public async Task GetReplyAsync_EmptyHistory_Throws()
    {
        _userMock.Setup(u => u.IsAuthenticated).Returns(true);
        var service = CreateServiceWithMockHttp(new HttpResponseMessage());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetReplyAsync(new List<ChatMessage>()));
    }

    [Fact]
    public async Task GetReplyAsync_NullHistory_Throws()
    {
        _userMock.Setup(u => u.IsAuthenticated).Returns(true);
        var service = CreateServiceWithMockHttp(new HttpResponseMessage());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetReplyAsync(null!));
    }

    [Fact]
    public async Task GetReplyAsync_NoUserMessage_Throws()
    {
        _userMock.Setup(u => u.IsAuthenticated).Returns(true);
        var service = CreateServiceWithMockHttp(new HttpResponseMessage());

        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "I'm the AI", Guid.NewGuid())
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetReplyAsync(messages));
    }

    // ---- API Key ----

    [Fact]
    public async Task GetReplyAsync_MissingApiKey_Throws()
    {
        _userMock.Setup(u => u.IsAuthenticated).Returns(true);

        var emptyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var service = new ChatService(new HttpClient(), emptyConfig, _userMock.Object, Mock.Of<ILogger<ChatService>>());

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello", Guid.NewGuid())
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetReplyAsync(messages));
    }

    // ---- Successful API Call ----

    [Fact]
    public async Task GetReplyAsync_SuccessfulResponse_ReturnsContent()
    {
        _userMock.Setup(u => u.IsAuthenticated).Returns(true);

        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new { content = "Hello from AI!" }
                }
            }
        });

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        };

        var service = CreateServiceWithMockHttp(response);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hi", Guid.NewGuid())
        };

        var reply = await service.GetReplyAsync(messages);

        Assert.Equal("Hello from AI!", reply.Content);
    }

    // ---- Cancellation ----

    [Fact]
    public async Task GetReplyAsync_Cancelled_ReturnsStoppedMessage()
    {
        _userMock.Setup(u => u.IsAuthenticated).Returns(true);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        var httpClient = new HttpClient(handlerMock.Object);
        var service = new ChatService(httpClient, _config, _userMock.Object, Mock.Of<ILogger<ChatService>>());

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hi", Guid.NewGuid())
        };

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var reply = await service.GetReplyAsync(messages, cts.Token);

        Assert.Contains("stopped by user", reply.Content);
    }

    // ---- API Error ----

    [Fact]
    public async Task GetReplyAsync_ApiError_ThrowsWithStatusCode()
    {
        _userMock.Setup(u => u.IsAuthenticated).Returns(true);

        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("Rate limited", Encoding.UTF8, "text/plain")
        };

        var service = CreateServiceWithMockHttp(response);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hi", Guid.NewGuid())
        };

        var ex = await Assert.ThrowsAsync<Exception>(
            () => service.GetReplyAsync(messages));

        Assert.Contains("TooManyRequests", ex.Message);
    }
}
