using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Workit.Shared.Payday;

namespace Workit.Tests.Payday;

public class PaydayTokenServiceTests : IDisposable
{
    private readonly string _testClientId = $"test-client-{Guid.NewGuid()}"; // unique per test to avoid cache collisions

    public void Dispose()
    {
        // Clean up any cached tokens from our test
        PaydayTokenService.InvalidateCache(_testClientId);
    }

    private static IOptions<PaydayOptions> CreateOptions(string? clientId = null, string? clientSecret = null)
    {
        return Options.Create(new PaydayOptions
        {
            ClientId = clientId ?? "",
            ClientSecret = clientSecret ?? ""
        });
    }

    private static IHttpClientFactory CreateFactory(HttpMessageHandler handler)
    {
        var mock = new Mock<IHttpClientFactory>();
        mock.Setup(f => f.CreateClient("PaydayApi"))
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("https://payday.test/") });
        return mock.Object;
    }

    private static HttpMessageHandler CreateSuccessHandler(string token = "test-token-abc")
    {
        return new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { accessToken = token })
        });
    }

    private static HttpMessageHandler CreateFailureHandler()
    {
        return new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }

    private static HttpMessageHandler CreateThrowingHandler()
    {
        return new MockHandler(_ => throw new HttpRequestException("Connection refused"));
    }

    // --- SetCredentials + GetTokenAsync ---

    [Fact]
    public async Task GetTokenAsync_WithSetCredentials_FetchesToken()
    {
        var handler = CreateSuccessHandler("fresh-token");
        var service = new PaydayTokenService(CreateFactory(handler), CreateOptions());
        service.SetCredentials(_testClientId, "secret");

        var token = await service.GetTokenAsync();

        token.Should().Be("fresh-token");
    }

    [Fact]
    public async Task GetTokenAsync_NoCredentials_ReturnsNull()
    {
        var handler = CreateSuccessHandler();
        var service = new PaydayTokenService(CreateFactory(handler), CreateOptions());

        var token = await service.GetTokenAsync();

        token.Should().BeNull();
    }

    [Fact]
    public async Task GetTokenAsync_UseFallbackOptions_WhenNoSetCredentials()
    {
        var fallbackClientId = $"fallback-{Guid.NewGuid()}";
        var handler = CreateSuccessHandler("fallback-token");
        var service = new PaydayTokenService(
            CreateFactory(handler),
            CreateOptions(fallbackClientId, "fallback-secret"));

        var token = await service.GetTokenAsync();

        token.Should().Be("fallback-token");

        // cleanup
        PaydayTokenService.InvalidateCache(fallbackClientId);
    }

    [Fact]
    public async Task GetTokenAsync_CachesToken_OnSecondCall()
    {
        var callCount = 0;
        var handler = new MockHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { accessToken = "cached-token" })
            };
        });
        var service = new PaydayTokenService(CreateFactory(handler), CreateOptions());
        service.SetCredentials(_testClientId, "secret");

        var token1 = await service.GetTokenAsync();
        var token2 = await service.GetTokenAsync();

        token1.Should().Be("cached-token");
        token2.Should().Be("cached-token");
        callCount.Should().Be(1, "second call should use cache");
    }

    [Fact]
    public async Task GetTokenAsync_AuthFailure_ReturnsNull()
    {
        var handler = CreateFailureHandler();
        var service = new PaydayTokenService(CreateFactory(handler), CreateOptions());
        service.SetCredentials(_testClientId, "wrong-secret");

        var token = await service.GetTokenAsync();

        token.Should().BeNull();
    }

    [Fact]
    public async Task GetTokenAsync_NetworkError_ReturnsNull()
    {
        var handler = CreateThrowingHandler();
        var service = new PaydayTokenService(CreateFactory(handler), CreateOptions());
        service.SetCredentials(_testClientId, "secret");

        var token = await service.GetTokenAsync();

        token.Should().BeNull();
    }

    // --- TestCredentialsAsync ---

    [Fact]
    public async Task TestCredentialsAsync_ValidCredentials_ReturnsToken()
    {
        var handler = CreateSuccessHandler("test-result-token");
        var service = new PaydayTokenService(CreateFactory(handler), CreateOptions());

        var token = await service.TestCredentialsAsync("any-client", "any-secret");

        token.Should().Be("test-result-token");
    }

    [Fact]
    public async Task TestCredentialsAsync_InvalidCredentials_ReturnsNull()
    {
        var handler = CreateFailureHandler();
        var service = new PaydayTokenService(CreateFactory(handler), CreateOptions());

        var token = await service.TestCredentialsAsync("bad-client", "bad-secret");

        token.Should().BeNull();
    }

    [Fact]
    public async Task TestCredentialsAsync_DoesNotAffectCache()
    {
        var callCount = 0;
        var handler = new MockHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { accessToken = $"token-{callCount}" })
            };
        });
        var service = new PaydayTokenService(CreateFactory(handler), CreateOptions());

        // TestCredentials should NOT cache
        await service.TestCredentialsAsync("test-only-client", "secret");

        // Subsequent GetTokenAsync with same credentials should make a new call
        service.SetCredentials("test-only-client", "secret");
        var token = await service.GetTokenAsync();

        callCount.Should().Be(2, "TestCredentials should not cache tokens");

        PaydayTokenService.InvalidateCache("test-only-client");
    }

    // --- InvalidateCache ---

    [Fact]
    public async Task InvalidateCache_ForcesRefreshOnNextCall()
    {
        var callCount = 0;
        var handler = new MockHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { accessToken = $"token-{callCount}" })
            };
        });
        var service = new PaydayTokenService(CreateFactory(handler), CreateOptions());
        service.SetCredentials(_testClientId, "secret");

        await service.GetTokenAsync(); // call 1, cached
        PaydayTokenService.InvalidateCache(_testClientId);
        await service.GetTokenAsync(); // call 2, cache invalidated

        callCount.Should().Be(2);
    }

    // Mock handler
    private class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

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
