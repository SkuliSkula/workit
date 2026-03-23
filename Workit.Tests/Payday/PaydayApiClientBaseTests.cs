using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Moq;
using Workit.Shared.Payday;

namespace Workit.Tests.Payday;

internal record PaydayTestItem(int Id, string Name);

internal class TestPaydayClient : PaydayApiClientBase
{
    public TestPaydayClient(IHttpClientFactory factory, IPaydayTokenService tokenService)
        : base(factory, tokenService) { }

    public Task<Shared.Api.ApiResult<T>> TestGetAsync<T>(string uri, string error)
        => GetAsync<T>(uri, error);

    public Task<Shared.Api.ApiResult<TResp>> TestPostForJsonAsync<TReq, TResp>(string uri, TReq payload, string error)
        => PostForJsonAsync<TReq, TResp>(uri, payload, error);

    public Task<Shared.Api.ApiResult<TResp>> TestPutForJsonAsync<TReq, TResp>(string uri, TReq payload, string error)
        => PutForJsonAsync<TReq, TResp>(uri, payload, error);

    public Task<Shared.Api.ApiResult<bool>> TestDeleteAsync(string uri, string error)
        => DeleteAsync(uri, error);

    public Task<Shared.Api.ApiResult<byte[]>> TestGetBytesAsync(string uri, string error)
        => GetBytesAsync(uri, error);
}

public class PaydayApiClientBaseTests
{
    private static (TestPaydayClient client, Mock<IPaydayTokenService> tokenMock) CreateClient(
        HttpMessageHandler handler, string? token = "test-token")
    {
        var tokenMock = new Mock<IPaydayTokenService>();
        tokenMock.Setup(t => t.GetTokenAsync()).ReturnsAsync(token);

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("PaydayApi"))
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("https://payday.test/") });

        return (new TestPaydayClient(factoryMock.Object, tokenMock.Object), tokenMock);
    }

    // --- GetAsync ---

    [Fact]
    public async Task GetAsync_Success_DeserializesWithWebDefaults()
    {
        // camelCase JSON to verify JsonSerializerDefaults.Web
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":42,"name":"Test"}""", System.Text.Encoding.UTF8, "application/json")
        });
        var (client, _) = CreateClient(handler);

        var result = await client.TestGetAsync<PaydayTestItem>("/items", "Failed");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(42);
        result.Value.Name.Should().Be("Test");
    }

    [Fact]
    public async Task GetAsync_ServerError_ReturnsErrorBody()
    {
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Server exploded")
        });
        var (client, _) = CreateClient(handler);

        var result = await client.TestGetAsync<PaydayTestItem>("/items", "Default error");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Server exploded");
    }

    [Fact]
    public async Task GetAsync_EmptyErrorBody_ReturnsDefaultMessage()
    {
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("")
        });
        var (client, _) = CreateClient(handler);

        var result = await client.TestGetAsync<PaydayTestItem>("/items", "Default error");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Default error");
    }

    [Fact]
    public async Task GetAsync_InvalidJson_ReturnsParseError()
    {
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>Not JSON</html>", System.Text.Encoding.UTF8, "application/json")
        });
        var (client, _) = CreateClient(handler);

        var result = await client.TestGetAsync<PaydayTestItem>("/items", "Failed");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Parse error");
        result.ErrorMessage.Should().Contain("Response:");
    }

    [Fact]
    public async Task GetAsync_LongInvalidJson_TruncatesPreviewAt300Chars()
    {
        var longHtml = new string('x', 500);
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(longHtml, System.Text.Encoding.UTF8, "application/json")
        });
        var (client, _) = CreateClient(handler);

        var result = await client.TestGetAsync<PaydayTestItem>("/items", "Failed");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Parse error");
        // Preview should be truncated (300 chars + "…")
        result.ErrorMessage.Should().Contain("…");
    }

    [Fact]
    public async Task GetAsync_Exception_ReturnsDefaultErrorWithMessage()
    {
        var handler = new MockHandler(_ => throw new HttpRequestException("Connection refused"));
        var (client, _) = CreateClient(handler);

        var result = await client.TestGetAsync<PaydayTestItem>("/items", "Default error");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Default error");
        result.ErrorMessage.Should().Contain("Connection refused");
    }

    // --- Auth header ---

    [Fact]
    public async Task Request_WithToken_SendsBearerHeader()
    {
        HttpRequestMessage? captured = null;
        var handler = new MockHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":1,"name":"x"}""", System.Text.Encoding.UTF8, "application/json")
            };
        });
        var (client, _) = CreateClient(handler, token: "my-payday-token");

        await client.TestGetAsync<PaydayTestItem>("/items", "Failed");

        captured!.Headers.Authorization.Should().NotBeNull();
        captured.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization.Parameter.Should().Be("my-payday-token");
    }

    [Fact]
    public async Task Request_WithoutToken_OmitsAuthHeader()
    {
        HttpRequestMessage? captured = null;
        var handler = new MockHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":1,"name":"x"}""", System.Text.Encoding.UTF8, "application/json")
            };
        });
        var (client, _) = CreateClient(handler, token: null);

        await client.TestGetAsync<PaydayTestItem>("/items", "Failed");

        captured!.Headers.Authorization.Should().BeNull();
    }

    // --- DeleteAsync ---

    [Fact]
    public async Task DeleteAsync_Success_ReturnsTrue()
    {
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var (client, _) = CreateClient(handler);

        var result = await client.TestDeleteAsync("/items/1", "Failed");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_Failure_ReturnsError()
    {
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("Not found")
        });
        var (client, _) = CreateClient(handler);

        var result = await client.TestDeleteAsync("/items/999", "Default error");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Not found");
    }

    [Fact]
    public async Task DeleteAsync_Exception_ReturnsDefaultError()
    {
        var handler = new MockHandler(_ => throw new Exception("boom"));
        var (client, _) = CreateClient(handler);

        var result = await client.TestDeleteAsync("/items/1", "Default error");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Default error");
    }

    // --- GetBytesAsync ---

    [Fact]
    public async Task GetBytesAsync_Success_ReturnsBytes()
    {
        var expectedBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expectedBytes)
        });
        var (client, _) = CreateClient(handler);

        var result = await client.TestGetBytesAsync("/pdf/1", "Failed");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(expectedBytes);
    }

    [Fact]
    public async Task GetBytesAsync_Failure_ReturnsError()
    {
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("Not found")
        });
        var (client, _) = CreateClient(handler);

        var result = await client.TestGetBytesAsync("/pdf/999", "Default error");

        result.IsSuccess.Should().BeFalse();
    }

    // --- PostForJsonAsync ---

    [Fact]
    public async Task PostForJsonAsync_Success_DeserializesResponse()
    {
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":99,"name":"Created"}""", System.Text.Encoding.UTF8, "application/json")
        });
        var (client, _) = CreateClient(handler);

        var result = await client.TestPostForJsonAsync<PaydayTestItem, PaydayTestItem>(
            "/items", new PaydayTestItem(0, "New"), "Failed");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(99);
    }

    // --- PutForJsonAsync ---

    [Fact]
    public async Task PutForJsonAsync_Success_DeserializesResponse()
    {
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":1,"name":"Updated"}""", System.Text.Encoding.UTF8, "application/json")
        });
        var (client, _) = CreateClient(handler);

        var result = await client.TestPutForJsonAsync<PaydayTestItem, PaydayTestItem>(
            "/items/1", new PaydayTestItem(1, "Updated"), "Failed");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Updated");
    }

    // Mock handler
    private class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;
        public MockHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) => _factory = factory;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_factory(request));
    }
}
