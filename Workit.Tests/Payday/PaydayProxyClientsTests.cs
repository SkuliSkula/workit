using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Workit.Shared.Api;
using Workit.Shared.Payday;

namespace Workit.Tests.Payday;

/// <summary>
/// The proxy clients must talk only to the Workit API (/api/payday/*) using the
/// user's Workit token — never to Payday, and never with Payday credentials.
/// </summary>
public class PaydayProxyClientsTests
{
    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    private sealed class FixedToken(string? token) : IAccessTokenAccessor
    {
        public ValueTask<string?> GetAccessTokenAsync() => ValueTask.FromResult(token);
    }

    private static (HttpClient client, CapturingHandler handler) CreateHttp(HttpResponseMessage response)
    {
        var handler = new CapturingHandler(response);
        return (new HttpClient(handler) { BaseAddress = new Uri("https://workit.test/") }, handler);
    }

    [Fact]
    public async Task Invoices_GetAll_CallsWorkitApi_WithWorkitToken_AndAllQueryParameters()
    {
        var (http, handler) = CreateHttp(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PaydayInvoicesResponse())
        });
        var api = new PaydayInvoicesProxy(http, new FixedToken("workit-jwt"));

        var customerId = Guid.NewGuid();
        var result = await api.GetAllAsync(page: 2, perPage: 50, include: "lines,payments",
            dateFrom: "2026-01-01", dateTo: "2026-01-31", excludeStatus: "DRAFT",
            customerId: customerId, query: "Ármúli 1", order: "asc", orderBy: "date");

        result.IsSuccess.Should().BeTrue();
        var req = handler.LastRequest!;
        req.Method.Should().Be(HttpMethod.Get);
        req.RequestUri!.Host.Should().Be("workit.test");
        req.RequestUri.AbsolutePath.Should().Be("/api/payday/invoices");
        req.RequestUri.Query.Should().Contain("page=2")
            .And.Contain("perPage=50")
            .And.Contain("include=lines%2Cpayments")
            .And.Contain("dateFrom=2026-01-01")
            .And.Contain("dateTo=2026-01-31")
            .And.Contain("excludeStatus=DRAFT")
            .And.Contain($"customerId={customerId}")
            .And.Contain("query=%C3%81rm%C3%BAli%201")
            .And.Contain("order=asc")
            .And.Contain("orderBy=date");
        req.Headers.Authorization!.Scheme.Should().Be("Bearer");
        req.Headers.Authorization.Parameter.Should().Be("workit-jwt");
    }

    [Fact]
    public async Task Invoices_GetAll_OmitsNullQueryParameters()
    {
        var (http, handler) = CreateHttp(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PaydayInvoicesResponse())
        });
        var api = new PaydayInvoicesProxy(http, new FixedToken("t"));

        await api.GetAllAsync();

        var query = handler.LastRequest!.RequestUri!.Query;
        query.Should().NotContain("dateFrom").And.NotContain("customerId").And.NotContain("query=");
        query.Should().Contain("page=1").And.Contain("include=lines");
    }

    [Fact]
    public async Task Employees_Delete_ReturnsTypedSuccess()
    {
        var (http, handler) = CreateHttp(new HttpResponseMessage(HttpStatusCode.OK));
        var api = new PaydayEmployeesProxy(http, new FixedToken("t"));

        var result = await api.DeleteAsync("emp/1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/api/payday/employees/emp%2F1");
    }

    [Fact]
    public async Task Invoices_GetPdf_ReturnsBytes()
    {
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF
        var (http, handler) = CreateHttp(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        });
        var api = new PaydayInvoicesProxy(http, new FixedToken("t"));

        var result = await api.GetPdfAsync("inv-1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(bytes);
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/api/payday/invoices/inv-1/pdf");
    }

    [Fact]
    public async Task ApiFailure_SurfacesServerMessage()
    {
        var (http, _) = CreateHttp(new HttpResponseMessage(HttpStatusCode.PreconditionFailed)
        {
            Content = new StringContent("Payday is not connected for this company.")
        });
        var api = new PaydayCompaniesProxy(http, new FixedToken("t"));

        var result = await api.GetMeAsync();

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Payday is not connected for this company.");
    }
}
