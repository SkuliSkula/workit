namespace Workit.Shared.Models;

/// <summary>
/// A company's own grouping for products and materials — Payday's list is
/// flat. Products and materials carry the category <em>name</em>; renaming a
/// category renames it everywhere, deleting one moves or refuses.
/// </summary>
public sealed class ProductCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A category with how much hangs off it, for the Categories page.</summary>
public sealed record ProductCategoryRow(Guid Id, string Name, int SortOrder, int Products, int Materials);

public sealed record ProductCategoryRequest(string Name);
