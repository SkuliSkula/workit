using Workit.Shared.Models;

namespace Workit.Shared.Api;

public interface IInvoicesApi
{
    Task<ApiResult<EmailSettings>>        GetEmailSettingsAsync();
    Task<ApiResult<EmailSettings>>        SaveEmailSettingsAsync(EmailSettings settings);
    Task<ApiResult<object>>               TestEmailConnectionAsync(EmailSettings settings);
    Task<ApiResult<object>>               ScanNowAsync();
    Task<ApiResult<List<VendorInvoice>>>  GetInvoicesAsync();
    Task<ApiResult<VendorInvoice>>        GetInvoiceAsync(Guid id);
}

public sealed class InvoicesApi(HttpClient httpClient, IAccessTokenAccessor tokenAccessor)
    : ApiClientBase(httpClient, tokenAccessor), IInvoicesApi
{
    public Task<ApiResult<EmailSettings>> GetEmailSettingsAsync() =>
        GetAsync<EmailSettings>("/api/email-settings", "Failed to load email settings.");

    public Task<ApiResult<EmailSettings>> SaveEmailSettingsAsync(EmailSettings settings) =>
        PutForJsonAsync<EmailSettings, EmailSettings>("/api/email-settings", settings, "Failed to save email settings.");

    public Task<ApiResult<object>> TestEmailConnectionAsync(EmailSettings settings) =>
        PostForJsonAsync<EmailSettings, object>("/api/email-settings/test", settings, "Connection test failed.");

    public Task<ApiResult<object>> ScanNowAsync() =>
        PostForJsonAsync<object?, object>("/api/invoices/scan", null, "Scan failed.");

    public Task<ApiResult<List<VendorInvoice>>> GetInvoicesAsync() =>
        GetAsync<List<VendorInvoice>>("/api/invoices", "Failed to load invoices.");

    public Task<ApiResult<VendorInvoice>> GetInvoiceAsync(Guid id) =>
        GetAsync<VendorInvoice>($"/api/invoices/{id}", "Failed to load invoice.");
}
