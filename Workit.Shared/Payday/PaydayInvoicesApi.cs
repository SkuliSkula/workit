using Workit.Shared.Api;

namespace Workit.Shared.Payday;

public interface IPaydayInvoicesApi
{
    /// <summary>Fetch a paginated list of invoices. Use include="lines", "payments" or "lines,payments".</summary>
    Task<ApiResult<PaydayInvoicesResponse>> GetAllAsync(
        int      page          = 1,
        int      perPage       = 25,
        string?  include       = "lines",
        string?  dateFrom      = null,
        string?  dateTo        = null,
        string?  excludeStatus = null,
        Guid?    customerId    = null,
        string?  query         = null,
        string   order         = "desc",
        string   orderBy       = "number");

    Task<ApiResult<PaydayInvoice>>  GetByIdAsync(string invoiceId, string? include = "lines,payments");
    Task<ApiResult<PaydayInvoice>>  CreateAsync(CreateInvoiceRequest request);
    Task<ApiResult<PaydayInvoice>>  UpdateAsync(string invoiceId, UpdateInvoiceRequest request);
    Task<ApiResult<bool>>           DeleteAsync(string invoiceId);
    Task<ApiResult<byte[]>>         GetPdfAsync(string invoiceId);
    Task<ApiResult<byte[]>>         GetAttachmentAsync(string invoiceId);
}

internal sealed class PaydayInvoicesApi(IHttpClientFactory httpClientFactory, IPaydayTokenService tokenService)
    : PaydayApiClientBase(httpClientFactory, tokenService), IPaydayInvoicesApi
{
    public Task<ApiResult<PaydayInvoicesResponse>> GetAllAsync(
        int     page          = 1,
        int     perPage       = 25,
        string? include       = "lines",
        string? dateFrom      = null,
        string? dateTo        = null,
        string? excludeStatus = null,
        Guid?   customerId    = null,
        string? query         = null,
        string  order         = "desc",
        string  orderBy       = "number")
    {
        var qs = new List<string>
        {
            $"page={page}",
            $"perpage={perPage}",
            $"order={order}",
            $"orderBy={orderBy}"
        };

        if (!string.IsNullOrWhiteSpace(include))       qs.Add($"include={include}");
        if (!string.IsNullOrWhiteSpace(dateFrom))      qs.Add($"dateFrom={dateFrom}");
        if (!string.IsNullOrWhiteSpace(dateTo))        qs.Add($"dateTo={dateTo}");
        if (!string.IsNullOrWhiteSpace(excludeStatus)) qs.Add($"excludeStatus={excludeStatus}");
        if (!string.IsNullOrWhiteSpace(query))         qs.Add($"query={query}");
        if (customerId.HasValue)                        qs.Add($"customerId={customerId}");

        return GetAsync<PaydayInvoicesResponse>(
            $"invoices?{string.Join("&", qs)}",
            "Invoices could not be loaded right now.");
    }

    public Task<ApiResult<PaydayInvoice>> GetByIdAsync(string invoiceId, string? include = "lines,payments") =>
        GetAsync<PaydayInvoice>(
            string.IsNullOrWhiteSpace(include)
                ? $"invoices/{invoiceId}"
                : $"invoices/{invoiceId}?include={include}",
            "Invoice could not be loaded right now.");

    public Task<ApiResult<PaydayInvoice>> CreateAsync(CreateInvoiceRequest request) =>
        PostForJsonAsync<CreateInvoiceRequest, PaydayInvoice>(
            "invoices",
            request,
            "Invoice could not be created right now.");

    public Task<ApiResult<PaydayInvoice>> UpdateAsync(string invoiceId, UpdateInvoiceRequest request) =>
        PutForJsonAsync<UpdateInvoiceRequest, PaydayInvoice>(
            $"invoices/{invoiceId}",
            request,
            "Invoice could not be updated right now.");

    public Task<ApiResult<bool>> DeleteAsync(string invoiceId) =>
        DeleteAsync($"invoices/{invoiceId}", "Invoice could not be deleted right now.");

    public Task<ApiResult<byte[]>> GetPdfAsync(string invoiceId) =>
        GetBytesAsync($"invoices/{invoiceId}/pdf", "Invoice PDF could not be downloaded right now.");

    public Task<ApiResult<byte[]>> GetAttachmentAsync(string invoiceId) =>
        GetBytesAsync($"invoices/{invoiceId}/attachment", "Invoice attachment could not be downloaded right now.");
}
