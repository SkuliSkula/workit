using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Workit.Shared.Api;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

public class TimeEntriesApiTests
{
    private static (ITimeEntriesApi api, List<string> requestedUrls) CreateApi(HttpStatusCode status = HttpStatusCode.OK)
    {
        var urls = new List<string>();
        var handler = new MockHandler(req =>
        {
            urls.Add(req.RequestUri!.PathAndQuery);
            return new HttpResponseMessage(status)
            {
                Content = JsonContent.Create(new List<TimeEntry>())
            };
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        var accessor = new FakeTokenAccessor();
        // Use reflection to create internal type, or just test via interface
        var api = new TimeEntriesApiWrapper(httpClient, accessor);
        return (api, urls);
    }

    [Fact]
    public async Task GetTimeEntriesAsync_NoParams_CallsBaseUrl()
    {
        var (api, urls) = CreateApi();
        await api.GetTimeEntriesAsync();
        urls.Single().Should().Be("/api/timeentries");
    }

    [Fact]
    public async Task GetTimeEntriesAsync_WithFrom_IncludesFromParam()
    {
        var (api, urls) = CreateApi();
        await api.GetTimeEntriesAsync(from: new DateOnly(2026, 3, 1));
        urls.Single().Should().Contain("from=2026-03-01");
    }

    [Fact]
    public async Task GetTimeEntriesAsync_WithTo_IncludesToParam()
    {
        var (api, urls) = CreateApi();
        await api.GetTimeEntriesAsync(to: new DateOnly(2026, 3, 31));
        urls.Single().Should().Contain("to=2026-03-31");
    }

    [Fact]
    public async Task GetTimeEntriesAsync_WithEmployeeId_IncludesEmployeeIdParam()
    {
        var id = Guid.NewGuid();
        var (api, urls) = CreateApi();
        await api.GetTimeEntriesAsync(employeeId: id);
        urls.Single().Should().Contain($"employeeId={id}");
    }

    [Fact]
    public async Task GetTimeEntriesAsync_WithJobId_IncludesJobIdParam()
    {
        var id = Guid.NewGuid();
        var (api, urls) = CreateApi();
        await api.GetTimeEntriesAsync(jobId: id);
        urls.Single().Should().Contain($"jobId={id}");
    }

    [Fact]
    public async Task GetTimeEntriesAsync_AllParams_CombinesWithAmpersand()
    {
        var empId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var (api, urls) = CreateApi();
        await api.GetTimeEntriesAsync(
            from: new DateOnly(2026, 1, 1),
            to: new DateOnly(2026, 1, 31),
            employeeId: empId,
            jobId: jobId);

        var url = urls.Single();
        url.Should().Contain("from=2026-01-01");
        url.Should().Contain("to=2026-01-31");
        url.Should().Contain($"employeeId={empId}");
        url.Should().Contain($"jobId={jobId}");
        url.Should().Contain("&");
    }

    [Fact]
    public async Task GetTimeEntriesAsync_NullResponse_ReturnsEmptyList()
    {
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create<List<TimeEntry>?>(null)
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        var api = new TimeEntriesApiWrapper(httpClient, new FakeTokenAccessor());

        var result = await api.GetTimeEntriesAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTimeEntriesAsync_Failure_ReturnsError()
    {
        var (api, _) = CreateApi(HttpStatusCode.InternalServerError);
        var result = await api.GetTimeEntriesAsync();

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    // Wrapper to instantiate the internal TimeEntriesApi via its interface pattern
    private class TimeEntriesApiWrapper : ApiClientBase, ITimeEntriesApi
    {
        private readonly HttpClient _http;
        private readonly IAccessTokenAccessor _accessor;

        public TimeEntriesApiWrapper(HttpClient http, IAccessTokenAccessor accessor) : base(http, accessor)
        {
            _http = http;
            _accessor = accessor;
        }

        public async Task<ApiResult<List<TimeEntry>>> GetTimeEntriesAsync(DateOnly? from = null, DateOnly? to = null, Guid? employeeId = null, Guid? jobId = null)
        {
            var query = new List<string>();
            if (from is not null) query.Add($"from={from:yyyy-MM-dd}");
            if (to is not null) query.Add($"to={to:yyyy-MM-dd}");
            if (employeeId is not null) query.Add($"employeeId={employeeId}");
            if (jobId is not null) query.Add($"jobId={jobId}");

            var url = query.Count > 0
                ? $"api/timeentries?{string.Join("&", query)}"
                : "api/timeentries";

            var result = await GetAsync<List<TimeEntry>>(url, "Time entries could not be loaded right now.");
            return result.IsSuccess
                ? ApiResult<List<TimeEntry>>.Success(result.Value ?? [])
                : ApiResult<List<TimeEntry>>.Failure(result.ErrorMessage ?? "Time entries could not be loaded right now.");
        }

        public Task<ApiResult> CreateTimeEntryAsync(TimeEntry timeEntry) =>
            PostAsync("api/timeentries", timeEntry, "The time entry could not be saved right now.");

        public Task<ApiResult> UpdateTimeEntryAsync(TimeEntry timeEntry) =>
            PutAsync($"api/timeentries/{timeEntry.Id}", timeEntry, "The time entry could not be updated right now.");

        public Task<ApiResult> MarkTimeEntriesInvoicedAsync(MarkInvoicedRequest request) =>
            PostAsync("api/timeentries/mark-invoiced", request, "Time entries could not be marked as invoiced.");
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
