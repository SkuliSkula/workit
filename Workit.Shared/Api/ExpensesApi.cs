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
}
