using Workit.Shared.Models;

namespace Workit.Shared.Api;

/// <summary>
/// Job attachment metadata returned to clients. Deliberately excludes the
/// storage key — files are only reachable through the download endpoint.
/// </summary>
public sealed class JobAttachmentInfo
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public AttachmentKind Kind { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }
}
