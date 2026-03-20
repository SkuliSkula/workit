using Workit.Shared.Api;

namespace Workit.Shared.Payday;

public interface IPaydayExpensesApi
{
    /// <summary>Create an expense with status DRAFT, UNPAID or PAID.</summary>
    Task<ApiResult<PaydayExpense>> CreateAsync(CreateExpenseRequest request);

    /// <summary>Update an existing expense.</summary>
    Task<ApiResult<PaydayExpense>> UpdateAsync(string expenseId, UpdateExpenseRequest request);

    /// <summary>Delete an expense.</summary>
    Task<ApiResult<bool>> DeleteAsync(string expenseId);

    /// <summary>Fetch a paginated list of expenses.</summary>
    Task<ApiResult<PaydayExpensesResponse>> GetAllAsync(
        int     page    = 1,
        int     perPage = 25,
        string? include = "lines",
        string? dateFrom = null,
        string? dateTo   = null,
        string? status   = null,
        string  order    = "desc",
        string  orderBy  = "date");

    /// <summary>Fetch a single expense by ID.</summary>
    Task<ApiResult<PaydayExpense>> GetByIdAsync(string expenseId, string? include = "lines");

    /// <summary>Download the expense attachment/receipt.</summary>
    Task<ApiResult<byte[]>> GetAttachmentAsync(string expenseId);

    /// <summary>Fetch available payment types for expenses.</summary>
    Task<ApiResult<List<PaydayPaymentTypeRef>>> GetPaymentTypesAsync();

    /// <summary>Fetch available accounts for expense lines.</summary>
    Task<ApiResult<List<PaydayExpenseAccount>>> GetAccountsAsync();
}

internal sealed class PaydayExpensesApi(IHttpClientFactory httpClientFactory, IPaydayTokenService tokenService)
    : PaydayApiClientBase(httpClientFactory, tokenService), IPaydayExpensesApi
{
    public Task<ApiResult<PaydayExpense>> CreateAsync(CreateExpenseRequest request) =>
        PostForJsonAsync<CreateExpenseRequest, PaydayExpense>(
            "expenses",
            request,
            "Expense could not be created right now.");

    public Task<ApiResult<PaydayExpense>> UpdateAsync(string expenseId, UpdateExpenseRequest request) =>
        PutForJsonAsync<UpdateExpenseRequest, PaydayExpense>(
            $"expenses/{expenseId}",
            request,
            "Expense could not be updated right now.");

    public Task<ApiResult<bool>> DeleteAsync(string expenseId) =>
        DeleteAsync($"expenses/{expenseId}", "Expense could not be deleted right now.");

    public Task<ApiResult<PaydayExpensesResponse>> GetAllAsync(
        int     page    = 1,
        int     perPage = 25,
        string? include = "lines",
        string? dateFrom = null,
        string? dateTo   = null,
        string? status   = null,
        string  order    = "desc",
        string  orderBy  = "date")
    {
        var qs = new List<string>
        {
            $"page={page}",
            $"perpage={perPage}",
            $"order={order}",
            $"orderBy={orderBy}"
        };

        if (!string.IsNullOrWhiteSpace(include))  qs.Add($"include={include}");
        if (!string.IsNullOrWhiteSpace(dateFrom)) qs.Add($"dateFrom={dateFrom}");
        if (!string.IsNullOrWhiteSpace(dateTo))   qs.Add($"dateTo={dateTo}");
        if (!string.IsNullOrWhiteSpace(status))   qs.Add($"status={status}");

        return GetAsync<PaydayExpensesResponse>(
            $"expenses?{string.Join("&", qs)}",
            "Expenses could not be loaded right now.");
    }

    public Task<ApiResult<PaydayExpense>> GetByIdAsync(string expenseId, string? include = "lines") =>
        GetAsync<PaydayExpense>(
            string.IsNullOrWhiteSpace(include)
                ? $"expenses/{expenseId}"
                : $"expenses/{expenseId}?include={include}",
            "Expense could not be loaded right now.");

    public Task<ApiResult<byte[]>> GetAttachmentAsync(string expenseId) =>
        GetBytesAsync($"expenses/{expenseId}/attachment", "Expense attachment could not be downloaded right now.");

    public Task<ApiResult<List<PaydayPaymentTypeRef>>> GetPaymentTypesAsync() =>
        GetAsync<List<PaydayPaymentTypeRef>>("expenses/paymenttypes", "Payment types could not be loaded right now.");

    public Task<ApiResult<List<PaydayExpenseAccount>>> GetAccountsAsync() =>
        GetAsync<List<PaydayExpenseAccount>>("expenses/accounts", "Accounts could not be loaded right now.");
}

/// <summary>Account reference returned by GET /expenses/accounts.</summary>
public sealed class PaydayExpenseAccount
{
    public Guid   Id     { get; set; }
    public string Name   { get; set; } = string.Empty;
    public int?   Number { get; set; }
}
