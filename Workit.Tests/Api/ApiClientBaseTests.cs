using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Workit.Shared.Api;

namespace Workit.Tests.Api;

// Concrete test implementation of the abstract ApiClientBase
internal class TestApiClient : ApiClientBase
{
    public TestApiClient(HttpClient httpClient, IAccessTokenAccessor accessor)
        : base(httpClient, accessor) { }

    public Task<ApiResult<T>> TestGetAsync<T>(string uri, string defaultError)
        => GetAsync<T>(uri, defaultError);

    public Task<ApiResult> TestPostAsync<T>(string uri, T payload, string defaultError)
        => PostAsync(uri, payload, defaultError);

    public Task<ApiResult> TestPutAsync<T>(string uri, T payload, string defaultError)
        => PutAsync(uri, payload, defaultError);

    public Task<ApiResult> TestDeleteAsync(string uri, string defaultError)
        => DeleteAsync(uri, defaultError);

    public Task<ApiResult<TResponse>> TestPostForJsonAsync<TRequest, TResponse>(string uri, TRequest payload, string defaultError)
        => PostForJsonAsync<TRequest, TResponse>(uri, payload, defaultError);
}

internal class FakeAccessTokenAccessor : IAccessTokenAccessor
{
    public string? Token { get; set; }
    public ValueTask<string?> GetAccessTokenAsync() => ValueTask.FromResult(Token);
}

internal record TestPayload(string Name);
internal record TestResponse(int Id, string Name);

public class ApiClientBaseTests
{
    private static (TestApiClient client, FakeAccessTokenAccessor accessor) CreateClient(HttpMessageHandler handler, string? token = null)
    {
        var accessor = new FakeAccessTokenAccessor { Token = token };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        return (new TestApiClient(httpClient, accessor), accessor);
    }

    // --- GET ---

    [Fact]
    public async Task GetAsync_Success_ReturnsDeserializedValue()
    {
        var handler = new MockHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TestResponse(1, "Test"))
        });
        var (client, _) = CreateClient(handler);

        var result = await client.TestGetAsync<TestResponse>("/api/test", "Failed");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(1);
        result.Value.Name.Should().Be("Test");
    }

    [Fact]
    public async Task GetAsync_ServerError_ReturnsFailureWithBody()
    {
        var handler = new MockHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Something went wrong")
        });
        var (client, _) = CreateClient(handler);

        var result = await client.TestGetAsync<TestResponse>("/api/test", "Default error");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Something went wrong");
    }

    [Fact]
    public async Task GetAsync_ServerErrorEmptyBody_ReturnsDefaultMessage()
    {
        var handler = new MockHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("")
        });
        var (client, _) = CreateClient(handler);

        var result = await client.TestGetAsync<TestResponse>("/api/test", "Default error");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Default error");
    }

    [Fact]
    public async Task GetAsync_ConnectionRefused_ReturnsNetworkError()
    {
        var handler = new MockHandler(_ => throw new HttpRequestException("Connection refused"));
        var (client, _) = CreateClient(handler);

        var result = await client.TestGetAsync<TestResponse>("/api/test", "Default error");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Could not reach the server");
    }

    [Fact]
    public async Task GetAsync_UnexpectedException_ReturnsCatchAllError()
    {
        var handler = new MockHandler(_ => throw new InvalidOperationException("Bad state"));
        var (client, _) = CreateClient(handler);

        var result = await client.TestGetAsync<TestResponse>("/api/test", "Default error");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Default error");
        result.ErrorMessage.Should().Contain("InvalidOperationException");
    }

    // --- POST ---

    [Fact]
    public async Task PostAsync_Success_ReturnsSuccess()
    {
        var handler = new MockHandler(new HttpResponseMessage(HttpStatusCode.Created));
        var (client, _) = CreateClient(handler);

        var result = await client.TestPostAsync("/api/test", new TestPayload("New"), "Failed");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PostAsync_Failure_ReturnsError()
    {
        var handler = new MockHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Validation error")
        });
        var (client, _) = CreateClient(handler);

        var result = await client.TestPostAsync("/api/test", new TestPayload("Bad"), "Default error");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Validation error");
    }

    // --- PUT ---

    [Fact]
    public async Task PutAsync_Success_ReturnsSuccess()
    {
        var handler = new MockHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var (client, _) = CreateClient(handler);

        var result = await client.TestPutAsync("/api/test/1", new TestPayload("Updated"), "Failed");

        result.IsSuccess.Should().BeTrue();
    }

    // --- DELETE ---

    [Fact]
    public async Task DeleteAsync_Success_ReturnsSuccess()
    {
        var handler = new MockHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var (client, _) = CreateClient(handler);

        var result = await client.TestDeleteAsync("/api/test/1", "Failed");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsFailure()
    {
        var handler = new MockHandler(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("Not found")
        });
        var (client, _) = CreateClient(handler);

        var result = await client.TestDeleteAsync("/api/test/999", "Default error");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Not found");
    }

    // --- PostForJson ---

    [Fact]
    public async Task PostForJsonAsync_Success_ReturnsDeserializedResponse()
    {
        var handler = new MockHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TestResponse(42, "Created"))
        });
        var (client, _) = CreateClient(handler);

        var result = await client.TestPostForJsonAsync<TestPayload, TestResponse>("/api/test", new TestPayload("New"), "Failed");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(42);
    }

    // --- Auth header ---

    [Fact]
    public async Task Request_WithToken_IncludesBearerHeader()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new MockHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new TestResponse(1, "Test"))
            };
        });
        var (client, _) = CreateClient(handler, token: "my-jwt-token");

        await client.TestGetAsync<TestResponse>("/api/test", "Failed");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.Authorization.Should().NotBeNull();
        capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be("my-jwt-token");
    }

    [Fact]
    public async Task Request_WithoutToken_OmitsAuthHeader()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new MockHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new TestResponse(1, "Test"))
            };
        });
        var (client, _) = CreateClient(handler, token: null);

        await client.TestGetAsync<TestResponse>("/api/test", "Failed");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.Authorization.Should().BeNull();
    }

    // Helper: simple mock HttpMessageHandler
    private class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

        public MockHandler(HttpResponseMessage response) : this(_ => response) { }

        public MockHandler(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _factory = factory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_factory(request));
        }
    }
}
