using Microsoft.Extensions.DependencyInjection;
using Workit.Shared.Api;

namespace Workit.Shared.Payday;

// ── Payday via the Workit API ─────────────────────────────────────────────────
//
// These implement the same IPayday*Api interfaces as the direct clients, but call
// the Workit API's /api/payday/* endpoints with the user's Workit token. The Workit
// API holds the company's Payday credentials and talks to Payday on the caller's
// behalf, so credentials never reach a client application.
//
// Register with AddPaydayProxyClients() in any app that talks to the Workit API.

public static class PaydayProxyServiceCollectionExtensions
{
    public static IServiceCollection AddPaydayProxyClients(this IServiceCollection services)
    {
        services.AddScoped<IPaydayUsersApi, PaydayUsersProxy>();
        services.AddScoped<IPaydayCompaniesApi, PaydayCompaniesProxy>();
        services.AddScoped<IPaydayCustomersApi, PaydayCustomersProxy>();
        services.AddScoped<IPaydayEmployeesApi, PaydayEmployeesProxy>();
        services.AddScoped<IPaydayPensionApi, PaydayPensionProxy>();
        services.AddScoped<IPaydayPayrollApi, PaydayPayrollProxy>();
        services.AddScoped<IPaydayInvoicesApi, PaydayInvoicesProxy>();
        services.AddScoped<IPaydayExpensesApi, PaydayExpensesProxy>();
        return services;
    }
}

internal static class PaydayProxyQuery
{
    internal static string Build(params (string Key, string? Value)[] parameters)
    {
        var parts = parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}")
            .ToList();
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}

internal sealed class PaydayUsersProxy(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IPaydayUsersApi
{
    public Task<ApiResult<PaydayUser>> GetMeAsync() =>
        GetAsync<PaydayUser>("api/payday/users/me", "Failed to fetch current user.");

    public Task<ApiResult<List<PaydayUser>>> GetAllAsync() =>
        GetAsync<List<PaydayUser>>("api/payday/users", "Failed to fetch users.");

    public Task<ApiResult<PaydayUser>> GetByIdAsync(string userId) =>
        GetAsync<PaydayUser>($"api/payday/users/{Uri.EscapeDataString(userId)}", "Failed to fetch user.");
}

internal sealed class PaydayCompaniesProxy(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IPaydayCompaniesApi
{
    public Task<ApiResult<PaydayCompany>> GetMeAsync() =>
        GetAsync<PaydayCompany>("api/payday/companies/me", "Failed to fetch company.");
}

internal sealed class PaydayCustomersProxy(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IPaydayCustomersApi
{
    public Task<ApiResult<PaydayCustomersResponse>> GetAllAsync(int page = 1, int perPage = 100) =>
        GetAsync<PaydayCustomersResponse>($"api/payday/customers?page={page}&perPage={perPage}", "Failed to fetch customers.");

    public Task<ApiResult<PaydayCustomer>> GetByIdAsync(string customerId) =>
        GetAsync<PaydayCustomer>($"api/payday/customers/{Uri.EscapeDataString(customerId)}", "Failed to fetch customer.");

    public Task<ApiResult<PaydayCustomer>> CreateAsync(CreateCustomerRequest request) =>
        PostForJsonAsync<CreateCustomerRequest, PaydayCustomer>("api/payday/customers", request, "Failed to create customer.");

    public Task<ApiResult<PaydayCustomer>> UpdateAsync(string customerId, UpdateCustomerRequest request) =>
        PutForJsonAsync<UpdateCustomerRequest, PaydayCustomer>($"api/payday/customers/{Uri.EscapeDataString(customerId)}", request, "Failed to update customer.");

    public Task<ApiResult<PaydayInvoicesResponse>> GetInvoicesAsync(string customerId, int page = 1, int perPage = 25) =>
        GetAsync<PaydayInvoicesResponse>($"api/payday/customers/{Uri.EscapeDataString(customerId)}/invoices?page={page}&perPage={perPage}", "The customer's invoices could not be loaded right now.");

    public Task<ApiResult<PaydayAccountStatement>> GetAccountStatementAsync(string customerId, string dateFrom, string dateTo, int page = 1, int perPage = 100) =>
        GetAsync<PaydayAccountStatement>($"api/payday/customers/{Uri.EscapeDataString(customerId)}/statement?dateFrom={dateFrom}&dateTo={dateTo}&page={page}&perPage={perPage}", "The account statement could not be loaded right now.");
}

internal sealed class PaydayEmployeesProxy(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IPaydayEmployeesApi
{
    public Task<ApiResult<List<PaydayEmployee>>> GetAllAsync() =>
        GetAsync<List<PaydayEmployee>>("api/payday/employees", "Failed to fetch employees.");

    public Task<ApiResult<PaydayEmployee>> GetByIdAsync(string employeeId) =>
        GetAsync<PaydayEmployee>($"api/payday/employees/{Uri.EscapeDataString(employeeId)}", "Failed to fetch employee.");

    public Task<ApiResult<PaydayEmployee>> CreateAsync(CreateEmployeeRequest request) =>
        PostForJsonAsync<CreateEmployeeRequest, PaydayEmployee>("api/payday/employees", request, "Failed to create employee.");

    public Task<ApiResult<PaydayEmployee>> UpdateAsync(string employeeId, UpdateEmployeeRequest request) =>
        PutForJsonAsync<UpdateEmployeeRequest, PaydayEmployee>($"api/payday/employees/{Uri.EscapeDataString(employeeId)}", request, "Failed to update employee.");

    public Task<ApiResult<bool>> DeleteAsync(string employeeId) =>
        DeleteForBoolAsync($"api/payday/employees/{Uri.EscapeDataString(employeeId)}", "Failed to delete employee.");
}

internal sealed class PaydayPensionProxy(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IPaydayPensionApi
{
    public Task<ApiResult<List<PaydayPensionFund>>> GetByTypeAsync(PaydayPensionType type) =>
        GetAsync<List<PaydayPensionFund>>($"api/payday/pension/funds/{(int)type}", "Failed to fetch pension funds.");
}

internal sealed class PaydayPayrollProxy(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IPaydayPayrollApi
{
    public Task<ApiResult<TimesheetUploadResult>> UploadTimesheetAsync(List<TimesheetEntry> entries) =>
        PostForJsonAsync<List<TimesheetEntry>, TimesheetUploadResult>("api/payday/payroll/timesheet", entries, "Failed to upload timesheet.");
}

internal sealed class PaydayInvoicesProxy(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IPaydayInvoicesApi
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
        var qs = PaydayProxyQuery.Build(
            ("page",          page.ToString()),
            ("perPage",       perPage.ToString()),
            ("include",       include),
            ("dateFrom",      dateFrom),
            ("dateTo",        dateTo),
            ("excludeStatus", excludeStatus),
            ("customerId",    customerId?.ToString()),
            ("query",         query),
            ("order",         order),
            ("orderBy",       orderBy));

        return GetAsync<PaydayInvoicesResponse>($"api/payday/invoices{qs}", "Invoices could not be loaded right now.");
    }

    public Task<ApiResult<PaydayInvoice>> GetByIdAsync(string invoiceId, string? include = "lines,payments") =>
        GetAsync<PaydayInvoice>(
            $"api/payday/invoices/{Uri.EscapeDataString(invoiceId)}{PaydayProxyQuery.Build(("include", include))}",
            "Invoice could not be loaded right now.");

    public Task<ApiResult<PaydayInvoice>> CreateAsync(CreateInvoiceRequest request) =>
        PostForJsonAsync<CreateInvoiceRequest, PaydayInvoice>("api/payday/invoices", request, "Invoice could not be created right now.");

    public Task<ApiResult<PaydayInvoice>> UpdateAsync(string invoiceId, UpdateInvoiceRequest request) =>
        PutForJsonAsync<UpdateInvoiceRequest, PaydayInvoice>($"api/payday/invoices/{Uri.EscapeDataString(invoiceId)}", request, "Invoice could not be updated right now.");

    public Task<ApiResult<bool>> DeleteAsync(string invoiceId) =>
        DeleteForBoolAsync($"api/payday/invoices/{Uri.EscapeDataString(invoiceId)}", "Invoice could not be deleted right now.");

    public Task<ApiResult<byte[]>> GetPdfAsync(string invoiceId) =>
        GetBytesAsync($"api/payday/invoices/{Uri.EscapeDataString(invoiceId)}/pdf", "Invoice PDF could not be downloaded right now.");

    public Task<ApiResult<byte[]>> GetAttachmentAsync(string invoiceId) =>
        GetBytesAsync($"api/payday/invoices/{Uri.EscapeDataString(invoiceId)}/attachment", "Invoice attachment could not be downloaded right now.");
}

internal sealed class PaydayExpensesProxy(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IPaydayExpensesApi
{
    public Task<ApiResult<PaydayExpense>> CreateAsync(CreateExpenseRequest request) =>
        PostForJsonAsync<CreateExpenseRequest, PaydayExpense>("api/payday/expenses", request, "Expense could not be created right now.");

    public Task<ApiResult<PaydayExpense>> UpdateAsync(string expenseId, UpdateExpenseRequest request) =>
        PutForJsonAsync<UpdateExpenseRequest, PaydayExpense>($"api/payday/expenses/{Uri.EscapeDataString(expenseId)}", request, "Expense could not be updated right now.");

    public Task<ApiResult<bool>> DeleteAsync(string expenseId) =>
        DeleteForBoolAsync($"api/payday/expenses/{Uri.EscapeDataString(expenseId)}", "Expense could not be deleted right now.");

    public Task<ApiResult<PaydayExpensesResponse>> GetAllAsync(
        int     page     = 1,
        int     perPage  = 25,
        string? include  = "lines",
        string? dateFrom = null,
        string? dateTo   = null,
        string? status   = null,
        string  order    = "desc",
        string  orderBy  = "date")
    {
        var qs = PaydayProxyQuery.Build(
            ("page",     page.ToString()),
            ("perPage",  perPage.ToString()),
            ("include",  include),
            ("dateFrom", dateFrom),
            ("dateTo",   dateTo),
            ("status",   status),
            ("order",    order),
            ("orderBy",  orderBy));

        return GetAsync<PaydayExpensesResponse>($"api/payday/expenses{qs}", "Expenses could not be loaded right now.");
    }

    public Task<ApiResult<PaydayExpense>> GetByIdAsync(string expenseId, string? include = "lines") =>
        GetAsync<PaydayExpense>(
            $"api/payday/expenses/{Uri.EscapeDataString(expenseId)}{PaydayProxyQuery.Build(("include", include))}",
            "Expense could not be loaded right now.");

    public Task<ApiResult<byte[]>> GetAttachmentAsync(string expenseId) =>
        GetBytesAsync($"api/payday/expenses/{Uri.EscapeDataString(expenseId)}/attachment", "Expense attachment could not be downloaded right now.");

    public Task<ApiResult<List<PaydayPaymentTypeRef>>> GetPaymentTypesAsync() =>
        GetAsync<List<PaydayPaymentTypeRef>>("api/payday/expenses/paymenttypes", "Payment types could not be loaded right now.");

    public Task<ApiResult<List<PaydayExpenseAccount>>> GetAccountsAsync() =>
        GetAsync<List<PaydayExpenseAccount>>("api/payday/expenses/accounts", "Accounts could not be loaded right now.");
}
