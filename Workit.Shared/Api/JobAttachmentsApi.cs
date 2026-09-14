namespace Workit.Shared.Api;

public interface IJobAttachmentsApi
{
    Task<ApiResult<List<JobAttachmentInfo>>> GetForJobAsync(Guid jobId);
    Task<ApiResult<JobAttachmentInfo?>> UploadAsync(Guid jobId, Stream content, string fileName, string contentType);
    Task<ApiResult<byte[]>> DownloadAsync(Guid jobId, Guid attachmentId);
    Task<ApiResult> DeleteAsync(Guid jobId, Guid attachmentId);

    /// <summary>Relative download URL for embedding (e.g. an &lt;img&gt; src via an authenticated proxy).</summary>
    string DownloadPath(Guid jobId, Guid attachmentId);
}

internal sealed class JobAttachmentsApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IJobAttachmentsApi
{
    public async Task<ApiResult<List<JobAttachmentInfo>>> GetForJobAsync(Guid jobId)
    {
        var result = await GetAsync<List<JobAttachmentInfo>>(
            $"api/jobs/{jobId}/attachments",
            "Attachments could not be loaded right now.");

        return result.IsSuccess
            ? ApiResult<List<JobAttachmentInfo>>.Success(result.Value ?? [])
            : ApiResult<List<JobAttachmentInfo>>.Failure(result.ErrorMessage ?? "Attachments could not be loaded right now.");
    }

    public Task<ApiResult<JobAttachmentInfo?>> UploadAsync(Guid jobId, Stream content, string fileName, string contentType) =>
        PostFileForJsonAsync<JobAttachmentInfo?>(
            $"api/jobs/{jobId}/attachments", content, fileName, contentType,
            "The file could not be uploaded right now.");

    public Task<ApiResult<byte[]>> DownloadAsync(Guid jobId, Guid attachmentId) =>
        GetBytesAsync($"api/jobs/{jobId}/attachments/{attachmentId}", "The file could not be downloaded right now.");

    public Task<ApiResult> DeleteAsync(Guid jobId, Guid attachmentId) =>
        DeleteAsync($"api/jobs/{jobId}/attachments/{attachmentId}", "The file could not be deleted right now.");

    public string DownloadPath(Guid jobId, Guid attachmentId) =>
        $"api/jobs/{jobId}/attachments/{attachmentId}";
}
