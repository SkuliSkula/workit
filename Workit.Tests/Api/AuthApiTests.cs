using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Workit.Shared.Api;
using Workit.Shared.Auth;

namespace Workit.Tests.Api;

/// <summary>
/// Pins the routes and verbs of the account-management calls. These exercise the
/// real <see cref="AuthApi"/> rather than a copy, so a typo'd route or a changed
/// verb fails here instead of silently 404ing against the running API.
/// </summary>
public class AuthApiTests
{
    private static (AuthApi api, List<(HttpMethod Method, string Url)> calls) CreateApi(
        HttpStatusCode status = HttpStatusCode.OK,
        object? responseBody = null)
    {
        var calls = new List<(HttpMethod, string)>();
        var handler = new MockHandler(req =>
        {
            calls.Add((req.Method, req.RequestUri!.PathAndQuery));
            var response = new HttpResponseMessage(status);
            if (responseBody is not null)
                response.Content = JsonContent.Create(responseBody);
            return response;
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        return (new AuthApi(httpClient, new FakeTokenAccessor()), calls);
    }

    [Fact]
    public async Task UpdateOwnerAsync_PutsToTheOwnerRoute()
    {
        var id = Guid.NewGuid();
        var (api, calls) = CreateApi();

        await api.UpdateOwnerAsync(id, new UpdateOwnerRequest { Name = "New Name", Email = "new@example.test" });

        calls.Single().Method.Should().Be(HttpMethod.Put);
        calls.Single().Url.Should().Be($"/api/auth/admin/owners/{id}");
    }

    [Fact]
    public async Task DeleteOwnerAsync_DeletesTheOwnerRoute()
    {
        var id = Guid.NewGuid();
        var (api, calls) = CreateApi(HttpStatusCode.NoContent);

        await api.DeleteOwnerAsync(id);

        calls.Single().Method.Should().Be(HttpMethod.Delete);
        calls.Single().Url.Should().Be($"/api/auth/admin/owners/{id}");
    }

    [Fact]
    public async Task ChangePasswordAsync_PostsToChangePassword()
    {
        var (api, calls) = CreateApi(responseBody: new LoginResponse { AccessToken = "token", Role = WorkitRoles.Owner });

        await api.ChangePasswordAsync(new ChangePasswordRequest
        {
            CurrentPassword = "old-password",
            NewPassword     = "new-password"
        });

        calls.Single().Method.Should().Be(HttpMethod.Post);
        calls.Single().Url.Should().Be("/api/auth/change-password");
    }

    /// <summary>
    /// The caller needs the re-issued session back: the API revokes every refresh
    /// token on a password change, so dropping this response logs the user out.
    /// </summary>
    [Fact]
    public async Task ChangePasswordAsync_ReturnsTheReissuedSession()
    {
        var (api, _) = CreateApi(responseBody: new LoginResponse
        {
            AccessToken  = "fresh-access-token",
            RefreshToken = "fresh-refresh-token",
            Role         = WorkitRoles.Owner,
            Email        = "owner@example.test"
        });

        var result = await api.ChangePasswordAsync(new ChangePasswordRequest
        {
            CurrentPassword = "old-password",
            NewPassword     = "new-password"
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("fresh-access-token");
        result.Value.RefreshToken.Should().Be("fresh-refresh-token");
    }

    [Fact]
    public async Task ForgotPasswordAsync_PostsToForgotPassword()
    {
        var (api, calls) = CreateApi();

        await api.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "owner@example.test" });

        calls.Single().Method.Should().Be(HttpMethod.Post);
        calls.Single().Url.Should().Be("/api/auth/forgot-password");
    }

    /// <summary>
    /// forgot-password answers 200 whether or not the address is registered, so the
    /// client must not turn a miss into an error the login page would show.
    /// </summary>
    [Fact]
    public async Task ForgotPasswordAsync_UnknownAddress_StillSucceeds()
    {
        var (api, _) = CreateApi(responseBody: new { message = "If that email is registered you will receive a reset link shortly." });

        var result = await api.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "nobody@example.test" });

        result.IsSuccess.Should().BeTrue();
    }

    private class FakeTokenAccessor : IAccessTokenAccessor
    {
        public ValueTask<string?> GetAccessTokenAsync() => ValueTask.FromResult<string?>(null);
    }

    private class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;
        public MockHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) => _factory = factory;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_factory(request));
    }
}
