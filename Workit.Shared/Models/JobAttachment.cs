namespace Workit.Shared.Models;

/// <summary>Broad category of an uploaded file, used for UI rendering (thumbnail vs. document icon).</summary>
public enum AttachmentKind
{
    Image = 0,
    Document = 1
}

/// <summary>
/// A file (photo or PDF) attached to a job. The bytes live in object storage;
/// this record is the metadata plus the storage key used to fetch them.
/// </summary>
public sealed class JobAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid JobId { get; set; }

    /// <summary>Original file name as uploaded, for display and download.</summary>
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public AttachmentKind Kind { get; set; }

    /// <summary>Opaque key of the object in storage. Never surfaced to clients.</summary>
    public string StorageKey { get; set; } = string.Empty;

    public Guid UploadedByUserId { get; set; }
    /// <summary>Display name of the uploader, captured at upload time.</summary>
    public string UploadedByName { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}
