namespace Workit.Shared.Api;

using Workit.Shared.Models;

public interface IPaydayExpenseLinkApi
{
    Task<ApiResult<List<PaydayExpenseLink>>> GetAllAsync();
    Task<ApiResult<List<PaydayExpenseLink>>> GetByJobAsync(Guid jobId);
    Task<ApiResult<List<PaydayExpenseLink>>> GetByExpenseAsync(Guid expenseId);
    Task<ApiResult<PaydayExpenseLink>> LinkAsync(Guid paydayExpenseId, Guid jobId, string? snapshotJson = null);
    Task<ApiResult> UnlinkAsync(Guid linkId);
}

internal sealed class PaydayExpenseLinkApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IPaydayExpenseLinkApi
{
    public async Task<ApiResult<List<PaydayExpenseLink>>> GetAllAsync()
    {
        var result = await GetAsync<List<PaydayExpenseLink>>(
            "api/payday-expense-links",
            "Expense links could not be loaded.");
        return result.IsSuccess
            ? ApiResult<List<PaydayExpenseLink>>.Success(result.Value ?? [])
            : ApiResult<List<PaydayExpenseLink>>.Failure(result.ErrorMessage ?? "Expense links could not be loaded.");
    }

    public async Task<ApiResult<List<PaydayExpenseLink>>> GetByJobAsync(Guid jobId)
    {
        var result = await GetAsync<List<PaydayExpenseLink>>(
            $"api/payday-expense-links?jobId={jobId}",
            "Expense links could not be loaded.");
        return result.IsSuccess
            ? ApiResult<List<PaydayExpenseLink>>.Success(result.Value ?? [])
            : ApiResult<List<PaydayExpenseLink>>.Failure(result.ErrorMessage ?? "Expense links could not be loaded.");
    }

    public async Task<ApiResult<List<PaydayExpenseLink>>> GetByExpenseAsync(Guid expenseId)
    {
        var result = await GetAsync<List<PaydayExpenseLink>>(
            $"api/payday-expense-links?expenseId={expenseId}",
            "Expense links could not be loaded.");
        return result.IsSuccess
            ? ApiResult<List<PaydayExpenseLink>>.Success(result.Value ?? [])
            : ApiResult<List<PaydayExpenseLink>>.Failure(result.ErrorMessage ?? "Expense links could not be loaded.");
    }

    public Task<ApiResult<PaydayExpenseLink>> LinkAsync(Guid paydayExpenseId, Guid jobId, string? snapshotJson = null) =>
        PostForJsonAsync<object, PaydayExpenseLink>(
            "api/payday-expense-links",
            new { PaydayExpenseId = paydayExpenseId, JobId = jobId, SnapshotJson = snapshotJson },
            "The expense could not be linked to the job.");

    public Task<ApiResult> UnlinkAsync(Guid linkId) =>
        DeleteAsync($"api/payday-expense-links/{linkId}", "The expense link could not be removed.");
}
