namespace Workit.Shared.Models;

public enum MaterialsMigrationAction
{
    /// <summary>A Payday product with the same SKU exists; the material is linked to it.</summary>
    Link = 0,
    /// <summary>No product has this SKU; one is created in Payday with Workit's stock as the opening balance.</summary>
    Create = 1,
    /// <summary>Already linked to a Payday product; nothing to do.</summary>
    AlreadyLinked = 2,
    /// <summary>Cannot be migrated as is — the message says why (no product code, duplicate code…).</summary>
    Conflict = 3,
}

public sealed record MaterialsMigrationRow(Guid MaterialId, string Name, string ProductCode, MaterialsMigrationAction Action, string? Sku, string? Message);

/// <summary>What the migration would do (dry run) or did, plus whether the company was switched over.</summary>
public sealed record MaterialsMigrationResult(
    bool DryRun, List<MaterialsMigrationRow> Rows, int Linked, int Created, int AlreadyLinked, int Conflicts, bool SwitchedOver, string? Error);
