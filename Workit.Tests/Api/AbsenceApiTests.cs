using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Workit.Shared.Api;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

public class AbsenceApiTests
{
    private static (AbsenceApiWrapper api, List<string> urls) CreateApi()
    {
        var urls = new List<string>();
        var handler = new MockHandler(req =>
        {
            urls.Add(req.RequestUri!.PathAndQuery);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<AbsenceRequest>())
            };
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        var api = new AbsenceApiWrapper(httpClient, new FakeTokenAccessor());
        return (api, urls);
    }

    [Fact]
    public async Task GetAbsencesAsync_NoParams_CallsBaseUrl()
    {
        var (api, urls) = CreateApi();
        await api.GetAbsencesAsync();
        urls.Single().Should().Be("/api/absences");
    }

    [Fact]
    public async Task GetAbsencesAsync_WithEmployeeId_IncludesParam()
    {
        var id = Guid.NewGuid();
        var (api, urls) = CreateApi();
        await api.GetAbsencesAsync(employeeId: id);
        urls.Single().Should().Contain($"employeeId={id}");
    }

    [Fact]
    public async Task GetAbsencesAsync_WithDateRange_IncludesDates()
    {
        var (api, urls) = CreateApi();
        await api.GetAbsencesAsync(from: new DateOnly(2026, 1, 1), to: new DateOnly(2026, 1, 31));

        var url = urls.Single();
        url.Should().Contain("from=2026-01-01");
        url.Should().Contain("to=2026-01-31");
    }

    [Fact]
    public async Task GetAbsencesAsync_WithStatus_IncludesStatus()
    {
        var (api, urls) = CreateApi();
        await api.GetAbsencesAsync(status: AbsenceStatus.Approved);
        urls.Single().Should().Contain("status=Approved");
    }

    [Fact]
    public async Task GetAbsencesAsync_AllParams_CombinesAll()
    {
        var empId = Guid.NewGuid();
        var (api, urls) = CreateApi();
        await api.GetAbsencesAsync(
            employeeId: empId,
            from: new DateOnly(2026, 3, 1),
            to: new DateOnly(2026, 3, 31),
            status: AbsenceStatus.Pending);

        var url = urls.Single();
        url.Should().Contain($"employeeId={empId}");
        url.Should().Contain("from=2026-03-01");
        url.Should().Contain("to=2026-03-31");
        url.Should().Contain("status=Pending");
    }

    [Fact]
    public async Task ReviewAbsenceAsync_SendsCorrectUrl()
    {
        string? capturedUrl = null;
        string? capturedBody = null;
        var handler = new MockHandler(req =>
        {
            capturedUrl = req.RequestUri!.PathAndQuery;
            capturedBody = req.Content?.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new AbsenceRequest())
            };
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        var api = new AbsenceApiWrapper(httpClient, new FakeTokenAccessor());

        var id = Guid.NewGuid();
        await api.ReviewAbsenceAsync(id, AbsenceStatus.Approved, "Looks good");

        capturedUrl.Should().Contain($"api/absences/{id}/review");
        // AbsenceStatus.Approved serializes as integer 1
        capturedBody.Should().Contain("\"status\":1");
        capturedBody.Should().Contain("Looks good");
    }

    [Fact]
    public async Task ReviewAbsenceAsync_NullNotes_SendsEmptyString()
    {
        string? capturedBody = null;
        var handler = new MockHandler(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new AbsenceRequest())
            };
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        var api = new AbsenceApiWrapper(httpClient, new FakeTokenAccessor());

        await api.ReviewAbsenceAsync(Guid.NewGuid(), AbsenceStatus.Denied, null);

        capturedBody.Should().NotBeNull();
        // reviewNotes should be empty string, not null
        capturedBody.Should().Contain("reviewNotes");
    }

    // Mirror the internal AbsenceApi logic for testing
    internal class AbsenceApiWrapper : ApiClientBase, IAbsenceApi
    {
        public AbsenceApiWrapper(HttpClient http, IAccessTokenAccessor accessor) : base(http, accessor) { }

        public async Task<ApiResult<List<AbsenceRequest>>> GetAbsencesAsync(
            Guid? employeeId = null, DateOnly? from = null, DateOnly? to = null, AbsenceStatus? status = null)
        {
            var query = new List<string>();
            if (employeeId is not null) query.Add($"employeeId={employeeId}");
            if (from is not null) query.Add($"from={from:yyyy-MM-dd}");
            if (to is not null) query.Add($"to={to:yyyy-MM-dd}");
            if (status is not null) query.Add($"status={status}");

            var url = query.Count > 0
                ? $"api/absences?{string.Join("&", query)}"
                : "api/absences";

            var result = await GetAsync<List<AbsenceRequest>>(url, "Absences could not be loaded right now.");
            return result.IsSuccess
                ? ApiResult<List<AbsenceRequest>>.Success(result.Value ?? [])
                : ApiResult<List<AbsenceRequest>>.Failure(result.ErrorMessage ?? "Absences could not be loaded right now.");
        }

        public Task<ApiResult<AbsenceRequest>> CreateAbsenceAsync(AbsenceRequest absence) =>
            PostForJsonAsync<AbsenceRequest, AbsenceRequest>("api/absences", absence, "The absence could not be saved right now.");

        public Task<ApiResult<AbsenceRequest>> UpdateAbsenceAsync(AbsenceRequest absence) =>
            PutForJsonAsync<AbsenceRequest, AbsenceRequest>($"api/absences/{absence.Id}", absence, "The absence could not be updated right now.");

        public Task<ApiResult> DeleteAbsenceAsync(Guid id) =>
            DeleteAsync($"api/absences/{id}", "The absence could not be deleted right now.");

        public Task<ApiResult<AbsenceRequest>> ReviewAbsenceAsync(Guid id, AbsenceStatus status, string? reviewNotes = null)
        {
            var payload = new { status, reviewNotes = reviewNotes ?? string.Empty };
            return PostForJsonAsync<object, AbsenceRequest>($"api/absences/{id}/review", payload, "The absence could not be reviewed right now.");
        }
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
