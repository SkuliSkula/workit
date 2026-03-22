using System.Net.Http;
using System.Net.Http.Json;
using Workit.Shared.Models;

namespace Workit.Shared.Api;

public interface IWorkDutyApi
{
    Task<ApiResult<WorkDutyResponse?>> GetWorkDutyAsync(int year, int month);
}

public sealed class WorkDutyApi(HttpClient http) : IWorkDutyApi
{
    public async Task<ApiResult<WorkDutyResponse?>> GetWorkDutyAsync(int year, int month)
    {
        try
        {
            var result = await http.GetFromJsonAsync<WorkDutyResponse>($"api/workduty?year={year}&month={month}");
            return ApiResult<WorkDutyResponse?>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResult<WorkDutyResponse?>.Failure(ex.Message);
        }
    }
}
