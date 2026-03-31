using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Workit.Shared.Api;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

public class MaterialsApiTests
{
    private static (MaterialsApiWrapper api, List<string> urls) CreateApi()
    {
        var urls = new List<string>();
        var handler = new MockHandler(req =>
        {
            urls.Add(req.RequestUri!.PathAndQuery);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<Material>())
            };
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        var api = new MaterialsApiWrapper(httpClient, new FakeTokenAccessor());
        return (api, urls);
    }

    [Fact]
    public async Task GetMaterialsAsync_NoParams_CallsBaseUrl()
    {
        var (api, urls) = CreateApi();
        await api.GetMaterialsAsync();
        urls.Single().Should().Be("/api/materials");
    }

    [Fact]
    public async Task GetMaterialsAsync_WithCategory_IncludesUriEncodedCategory()
    {
        var (api, urls) = CreateApi();
        await api.GetMaterialsAsync(category: "Rafstrengir & Kaplar");

        var url = urls.Single();
        url.Should().Contain("category=");
        url.Should().Contain("Rafstrengir");
        // & should be URI-escaped
        url.Should().NotContain("category=Rafstrengir &");
    }

    [Fact]
    public async Task GetMaterialsAsync_WithActiveOnly_IncludesActiveOnlyTrue()
    {
        var (api, urls) = CreateApi();
        await api.GetMaterialsAsync(activeOnly: true);
        urls.Single().Should().Contain("activeOnly=true");
    }

    [Fact]
    public async Task GetMaterialsAsync_ActiveOnlyFalse_DoesNotIncludeParam()
    {
        var (api, urls) = CreateApi();
        await api.GetMaterialsAsync(activeOnly: false);
        urls.Single().Should().NotContain("activeOnly");
    }

    [Fact]
    public async Task GetMaterialsAsync_BothParams_CombinesWithAmpersand()
    {
        var (api, urls) = CreateApi();
        await api.GetMaterialsAsync(category: "Cables", activeOnly: true);

        var url = urls.Single();
        url.Should().Contain("category=Cables");
        url.Should().Contain("activeOnly=true");
        url.Should().Contain("&");
    }

    [Fact]
    public async Task GetMaterialsAsync_WhitespaceCategory_IgnoresIt()
    {
        var (api, urls) = CreateApi();
        await api.GetMaterialsAsync(category: "  ");
        urls.Single().Should().Be("/api/materials");
    }

    [Fact]
    public async Task GetMaterialUsageAsync_NoJobId_CallsBaseUrl()
    {
        var urls = new List<string>();
        var handler = new MockHandler(req =>
        {
            urls.Add(req.RequestUri!.PathAndQuery);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<MaterialUsage>())
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        var api = new MaterialsApiWrapper(httpClient, new FakeTokenAccessor());

        await api.GetMaterialUsageAsync();
        urls.Single().Should().Be("/api/materials/usage");
    }

    [Fact]
    public async Task GetMaterialUsageAsync_WithJobId_IncludesJobId()
    {
        var jobId = Guid.NewGuid();
        var urls = new List<string>();
        var handler = new MockHandler(req =>
        {
            urls.Add(req.RequestUri!.PathAndQuery);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<MaterialUsage>())
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        var api = new MaterialsApiWrapper(httpClient, new FakeTokenAccessor());

        await api.GetMaterialUsageAsync(jobId);
        urls.Single().Should().Contain($"jobId={jobId}");
    }

    // Mirror internal MaterialsApi logic
    internal class MaterialsApiWrapper : ApiClientBase, IMaterialsApi
    {
        public MaterialsApiWrapper(HttpClient http, IAccessTokenAccessor accessor) : base(http, accessor) { }

        public async Task<ApiResult<List<Material>>> GetMaterialsAsync(string? category = null, bool activeOnly = false)
        {
            var url = "api/materials";
            var qs = new List<string>();
            if (!string.IsNullOrWhiteSpace(category)) qs.Add($"category={Uri.EscapeDataString(category)}");
            if (activeOnly) qs.Add("activeOnly=true");
            if (qs.Count > 0) url += "?" + string.Join("&", qs);

            var result = await GetAsync<List<Material>>(url, "Materials could not be loaded right now.");
            return result.IsSuccess
                ? ApiResult<List<Material>>.Success(result.Value ?? [])
                : ApiResult<List<Material>>.Failure(result.ErrorMessage ?? "Materials could not be loaded right now.");
        }

        public Task<ApiResult<Material>> CreateMaterialAsync(Material material) =>
            PostForJsonAsync<Material, Material>("api/materials", material, "The material could not be saved right now.");

        public Task<ApiResult<Material>> UpdateMaterialAsync(Guid id, Material material) =>
            PutForJsonAsync<Material, Material>($"api/materials/{id}", material, "The material could not be updated right now.");

        public Task<ApiResult> DeleteMaterialAsync(Guid id) =>
            DeleteAsync($"api/materials/{id}", "The material could not be deleted right now.");

        public async Task<ApiResult<List<MaterialUsage>>> GetMaterialUsageAsync(Guid? jobId = null)
        {
            var url = jobId is null ? "api/materials/usage" : $"api/materials/usage?jobId={jobId}";
            var result = await GetAsync<List<MaterialUsage>>(url, "Material usage could not be loaded right now.");
            return result.IsSuccess
                ? ApiResult<List<MaterialUsage>>.Success(result.Value ?? [])
                : ApiResult<List<MaterialUsage>>.Failure(result.ErrorMessage ?? "Material usage could not be loaded right now.");
        }

        public Task<ApiResult<MaterialUsage>> LogMaterialUsageAsync(Guid materialId, decimal quantity, Guid? jobId, string? notes)
        {
            var payload = new { materialId, quantity, jobId, notes };
            return PostForJsonAsync<object, MaterialUsage>("api/materials/usage", payload, "Material usage could not be logged right now.");
        }

        public Task<ApiResult> MarkMaterialUsageInvoicedAsync(MarkInvoicedRequest request) =>
            PostAsync("api/materials/usage/mark-invoiced", request, "Material usage could not be marked as invoiced.");

        public Task<ApiResult> MarkMaterialUsageUninvoicedAsync(MarkUninvoicedRequest request) =>
            PostAsync("api/materials/usage/mark-uninvoiced", request, "Material usage could not be unmarked as invoiced.");
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
