namespace Workit.Shared.Models;

/// <summary>
/// Who created a record and when. Stamped by the API on create and never
/// changed afterwards; clients may send these fields but they are ignored.
/// All three are null on records that predate the audit columns (added
/// 2026-09-17) and on seeded data.
/// </summary>
public interface ICreatedAudit
{
    DateTimeOffset? CreatedAt { get; set; }
    Guid? CreatedByUserId { get; set; }
    /// <summary>Display name of the creator, captured at creation so it survives renames and deletions.</summary>
    string? CreatedByName { get; set; }
}
