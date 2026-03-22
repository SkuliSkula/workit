using Workit.Shared.Models;

namespace Workit.Shared.Api;

public interface IAbsenceApi
{
    Task<ApiResult<List<AbsenceRequest>>> GetAbsencesAsync(
        Guid? employeeId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        AbsenceStatus? status = null);

    Task<ApiResult<AbsenceRequest>> CreateAbsenceAsync(AbsenceRequest absence);
    Task<ApiResult<AbsenceRequest>> UpdateAbsenceAsync(AbsenceRequest absence);
    Task<ApiResult> DeleteAbsenceAsync(Guid id);
    Task<ApiResult<AbsenceRequest>> ReviewAbsenceAsync(Guid id, AbsenceStatus status, string? reviewNotes = null);
}

internal sealed class AbsenceApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IAbsenceApi
{
    public async Task<ApiResult<List<AbsenceRequest>>> GetAbsencesAsync(
        Guid? employeeId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        AbsenceStatus? status = null)
    {
        var query = new List<string>();
        if (employeeId is not null) query.Add($"employeeId={employeeId}");
        if (from is not null) query.Add($"from={from:yyyy-MM-dd}");
        if (to is not null) query.Add($"to={to:yyyy-MM-dd}");
        if (status is not null) query.Add($"status={status}");

        var url = query.Count > 0
            ? $"api/absences?{string.Join("&", query)}"
            : "api/absences";

        var result = await GetAsync<List<AbsenceRequest>>(url, "Absences could not be loaded right now.");
        return result.IsSuccess
            ? ApiResult<List<AbsenceRequest>>.Success(result.Value ?? [])
            : ApiResult<List<AbsenceRequest>>.Failure(result.ErrorMessage ?? "Absences could not be loaded right now.");
    }

    public Task<ApiResult<AbsenceRequest>> CreateAbsenceAsync(AbsenceRequest absence) =>
        PostForJsonAsync<AbsenceRequest, AbsenceRequest>("api/absences", absence, "The absence could not be saved right now.");

    public Task<ApiResult<AbsenceRequest>> UpdateAbsenceAsync(AbsenceRequest absence) =>
        PutForJsonAsync<AbsenceRequest, AbsenceRequest>($"api/absences/{absence.Id}", absence, "The absence could not be updated right now.");

    public Task<ApiResult> DeleteAbsenceAsync(Guid id) =>
        DeleteAsync($"api/absences/{id}", "The absence could not be deleted right now.");

    public Task<ApiResult<AbsenceRequest>> ReviewAbsenceAsync(Guid id, AbsenceStatus status, string? reviewNotes = null)
    {
        var payload = new { status, reviewNotes = reviewNotes ?? string.Empty };
        return PostForJsonAsync<object, AbsenceRequest>($"api/absences/{id}/review", payload, "The absence could not be reviewed right now.");
    }
}
