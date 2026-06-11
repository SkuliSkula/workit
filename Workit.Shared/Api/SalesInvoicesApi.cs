using Workit.Shared.Models;

namespace Workit.Shared.Api;

public interface ISalesInvoicesApi
{
    Task<ApiResult<List<Invoice>>>    GetInvoicesAsync(string? status = null);
    Task<ApiResult<Invoice>>          GetInvoiceAsync(Guid id);
    Task<ApiResult<Invoice>>          CreateInvoiceAsync(Invoice invoice);
    Task<ApiResult<Invoice>>          UpdateInvoiceAsync(Invoice invoice);
    Task<ApiResult>                   DeleteInvoiceAsync(Guid id);
    Task<ApiResult<InvoiceLine>>      AddLineAsync(Guid invoiceId, InvoiceLine line);
    Task<ApiResult<InvoiceLine>>      UpdateLineAsync(Guid invoiceId, InvoiceLine line);
    Task<ApiResult>                   DeleteLineAsync(Guid invoiceId, Guid lineId);
    Task<ApiResult<InvoicePayment>>   AddPaymentAsync(Guid invoiceId, InvoicePayment payment);
    Task<ApiResult>                   DeletePaymentAsync(Guid invoiceId, Guid paymentId);
}

internal sealed class SalesInvoicesApi(HttpClient httpClient, IAccessTokenAccessor tokenAccessor)
    : ApiClientBase(httpClient, tokenAccessor), ISalesInvoicesApi
{
    public Task<ApiResult<List<Invoice>>> GetInvoicesAsync(string? status = null)
    {
        var url = status is null ? "/api/sales-invoices" : $"/api/sales-invoices?status={Uri.EscapeDataString(status)}";
        return GetAsync<List<Invoice>>(url, "Failed to load invoices.");
    }

    public Task<ApiResult<Invoice>> GetInvoiceAsync(Guid id) =>
        GetAsync<Invoice>($"/api/sales-invoices/{id}", "Failed to load invoice.");

    public Task<ApiResult<Invoice>> CreateInvoiceAsync(Invoice invoice) =>
        PostForJsonAsync<Invoice, Invoice>("/api/sales-invoices", invoice, "Failed to create invoice.");

    public Task<ApiResult<Invoice>> UpdateInvoiceAsync(Invoice invoice) =>
        PutForJsonAsync<Invoice, Invoice>($"/api/sales-invoices/{invoice.Id}", invoice, "Failed to update invoice.");

    public Task<ApiResult> DeleteInvoiceAsync(Guid id) =>
        DeleteAsync($"/api/sales-invoices/{id}", "Failed to delete invoice.");

    public Task<ApiResult<InvoiceLine>> AddLineAsync(Guid invoiceId, InvoiceLine line) =>
        PostForJsonAsync<InvoiceLine, InvoiceLine>($"/api/sales-invoices/{invoiceId}/lines", line, "Failed to add line.");

    public Task<ApiResult<InvoiceLine>> UpdateLineAsync(Guid invoiceId, InvoiceLine line) =>
        PutForJsonAsync<InvoiceLine, InvoiceLine>($"/api/sales-invoices/{invoiceId}/lines/{line.Id}", line, "Failed to update line.");

    public Task<ApiResult> DeleteLineAsync(Guid invoiceId, Guid lineId) =>
        DeleteAsync($"/api/sales-invoices/{invoiceId}/lines/{lineId}", "Failed to delete line.");

    public Task<ApiResult<InvoicePayment>> AddPaymentAsync(Guid invoiceId, InvoicePayment payment) =>
        PostForJsonAsync<InvoicePayment, InvoicePayment>($"/api/sales-invoices/{invoiceId}/payments", payment, "Failed to add payment.");

    public Task<ApiResult> DeletePaymentAsync(Guid invoiceId, Guid paymentId) =>
        DeleteAsync($"/api/sales-invoices/{invoiceId}/payments/{paymentId}", "Failed to delete payment.");
}
