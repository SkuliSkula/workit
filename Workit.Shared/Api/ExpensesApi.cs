using Workit.Shared.Models;

namespace Workit.Shared.Api;

public interface IExpensesApi
{
    Task<ApiResult<List<Expense>>>  GetExpensesAsync(string? status = null, Guid? jobId = null);
    Task<ApiResult<Expense>>        GetExpenseAsync(Guid id);
    Task<ApiResult<Expense>>        CreateExpenseAsync(Expense expense);
    Task<ApiResult<Expense>>        UpdateExpenseAsync(Expense expense);
    Task<ApiResult>                 DeleteExpenseAsync(Guid id);
    Task<ApiResult<ExpenseLine>>    AddLineAsync(Guid expenseId, ExpenseLine line);
    Task<ApiResult<ExpenseLine>>    UpdateLineAsync(Guid expenseId, ExpenseLine line);
    Task<ApiResult>                 DeleteLineAsync(Guid expenseId, Guid lineId);

    /// <summary>Import a Payday expense into the Workit DB. Idempotent — deduped by PaydayId.</summary>
    Task<ApiResult<Expense>>        ImportPaydayExpenseAsync(Expense expense);
    /// <summary>Links Payday expenses to the jobs whose codes appear on them; see ExpenseAutoLinkEndpoints.</summary>
    Task<ApiResult<ExpenseAutoLinkResult>> AutoLinkAsync(string? dateFrom = null, string? dateTo = null);

    /// <summary>Record partial/full billings of expense lines, tagged with an invoice number.</summary>
    Task<ApiResult>                 BillLinesAsync(BillExpenseLinesRequest request);
}

internal sealed class ExpensesApi(HttpClient httpClient, IAccessTokenAccessor tokenAccessor)
    : ApiClientBase(httpClient, tokenAccessor), IExpensesApi
{
    public Task<ApiResult<List<Expense>>> GetExpensesAsync(string? status = null, Guid? jobId = null)
    {
        var qs = new List<string>();
        if (status is not null)  qs.Add($"status={Uri.EscapeDataString(status)}");
        if (jobId.HasValue)      qs.Add($"jobId={jobId}");
        var url = qs.Count > 0 ? $"/api/expenses?{string.Join('&', qs)}" : "/api/expenses";
        return GetAsync<List<Expense>>(url, "Failed to load expenses.");
    }

    public Task<ApiResult<Expense>> GetExpenseAsync(Guid id) =>
        GetAsync<Expense>($"/api/expenses/{id}", "Failed to load expense.");

    public Task<ApiResult<Expense>> CreateExpenseAsync(Expense expense) =>
        PostForJsonAsync<Expense, Expense>("/api/expenses", expense, "Failed to create expense.");

    public Task<ApiResult<Expense>> UpdateExpenseAsync(Expense expense) =>
        PutForJsonAsync<Expense, Expense>($"/api/expenses/{expense.Id}", expense, "Failed to update expense.");

    public Task<ApiResult> DeleteExpenseAsync(Guid id) =>
        DeleteAsync($"/api/expenses/{id}", "Failed to delete expense.");

    public Task<ApiResult<ExpenseLine>> AddLineAsync(Guid expenseId, ExpenseLine line) =>
        PostForJsonAsync<ExpenseLine, ExpenseLine>($"/api/expenses/{expenseId}/lines", line, "Failed to add line.");

    public Task<ApiResult<ExpenseLine>> UpdateLineAsync(Guid expenseId, ExpenseLine line) =>
        PutForJsonAsync<ExpenseLine, ExpenseLine>($"/api/expenses/{expenseId}/lines/{line.Id}", line, "Failed to update line.");

    public Task<ApiResult> DeleteLineAsync(Guid expenseId, Guid lineId) =>
        DeleteAsync($"/api/expenses/{expenseId}/lines/{lineId}", "Failed to delete line.");

    public Task<ApiResult<Expense>> ImportPaydayExpenseAsync(Expense expense) =>
        PostForJsonAsync<Expense, Expense>("/api/expenses/import-payday", expense, "Failed to import Payday expense.");

    public Task<ApiResult<ExpenseAutoLinkResult>> AutoLinkAsync(string? dateFrom = null, string? dateTo = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(dateFrom)) query.Add($"dateFrom={Uri.EscapeDataString(dateFrom)}");
        if (!string.IsNullOrWhiteSpace(dateTo))   query.Add($"dateTo={Uri.EscapeDataString(dateTo)}");
        var url = "/api/expenses/auto-link" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        return PostForJsonAsync<object, ExpenseAutoLinkResult>(url, new { }, "Could not link expenses to jobs automatically.");
    }

    public Task<ApiResult> BillLinesAsync(BillExpenseLinesRequest request) =>
        PostAsync("/api/expenses/lines/bill", request, "Failed to record expense billing.");
}
