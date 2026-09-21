using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Payday;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

/// <summary>
/// The company's product/material categories. Products and materials store the
/// name, so a rename rewrites them and a delete either moves them or refuses.
/// The first read seeds the table from whatever names are already in use, so
/// a company that categorised before this existed loses nothing.
/// </summary>
internal static class ProductCategoryEndpoints
{
    internal static void MapProductCategoryEndpoints(this WebApplication app)
    {
        var logger = app.Logger;
        var group = app.MapGroup("/api/categories").RequireAuthorization().WithTags("Categories");

        group.MapGet("/", async (WorkitDbContext db, HttpContext http, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var user = http.User.ToUserContext();
                    await EnsureSeededAsync(db, user.CompanyId, ct);
                    return Results.Ok(await ListAsync(db, user.CompanyId, ct));
                }, logger, "loading categories"))
            .WithName("GetCategories");

        group.MapPost("/", async (WorkitDbContext db, HttpContext http, ProductCategoryRequest body, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!http.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var user = http.User.ToUserContext();
                    var name = Clean(body.Name);
                    if (name is null) return Results.BadRequest("Give the category a name.");
                    if (await ExistsAsync(db, user.CompanyId, name, null, ct)) return Results.Conflict($"There is already a category called {name}.");

                    var order = await db.ProductCategories.Where(c => c.CompanyId == user.CompanyId).Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? 0;
                    var category = new ProductCategory { CompanyId = user.CompanyId, Name = name, SortOrder = order + 1 };
                    db.ProductCategories.Add(category);
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/categories/{category.Id}", new ProductCategoryRow(category.Id, category.Name, category.SortOrder, 0, 0));
                }, logger, "creating a category"))
            .WithName("CreateCategory");

        // The supplier's taxonomy as a starting list; adds only what the company lacks.
        group.MapPost("/seed-standard", async (WorkitDbContext db, HttpContext http, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!http.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var user = http.User.ToUserContext();
                    var known = (await db.ProductCategories.Where(c => c.CompanyId == user.CompanyId).Select(c => c.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var order = await db.ProductCategories.Where(c => c.CompanyId == user.CompanyId).Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? 0;
                    var added = 0;
                    foreach (var (_, name) in ProductCategorizer.StandardCategories)
                    {
                        if (known.Contains(name)) continue;
                        db.ProductCategories.Add(new ProductCategory { CompanyId = user.CompanyId, Name = name, SortOrder = ++order });
                        known.Add(name); added++;
                    }
                    await db.SaveChangesAsync(ct);
                    return Results.Ok(new { added, total = known.Count });
                }, logger, "adding the standard categories"))
            .WithName("SeedStandardCategories");

        group.MapPut("/{id:guid}", async (WorkitDbContext db, HttpContext http, Guid id, ProductCategoryRequest body, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!http.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var user = http.User.ToUserContext();
                    var name = Clean(body.Name);
                    if (name is null) return Results.BadRequest("Give the category a name.");
                    var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == user.CompanyId, ct);
                    if (category is null) return Results.NotFound();
                    if (await ExistsAsync(db, user.CompanyId, name, id, ct)) return Results.Conflict($"There is already a category called {name}.");

                    var (products, materials) = await RenameAsync(db, user.CompanyId, category, name, ct);
                    return Results.Ok(new ProductCategoryRow(category.Id, category.Name, category.SortOrder, products, materials));
                }, logger, "renaming a category"))
            .WithName("RenameCategory");

        group.MapDelete("/{id:guid}", async (WorkitDbContext db, HttpContext http, Guid id, CancellationToken ct, Guid? moveTo = null) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!http.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var user = http.User.ToUserContext();
                    var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == user.CompanyId, ct);
                    if (category is null) return Results.NotFound();

                    var products  = await db.PaydayProducts.CountAsync(p => p.CompanyId == user.CompanyId && p.Category == category.Name, ct);
                    var materials = await db.Materials.CountAsync(m => m.CompanyId == user.CompanyId && m.Category == category.Name, ct);
                    if (products + materials > 0)
                    {
                        if (moveTo is null || moveTo == id)
                            return Results.Conflict($"{category.Name} is on {Describe(products, materials)}. Pick a category to move them to first.");
                        var target = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == moveTo && c.CompanyId == user.CompanyId, ct);
                        if (target is null) return Results.BadRequest("The category to move to does not exist.");
                        await Reassign(db, user.CompanyId, category.Name, target.Name, ct);
                    }

                    db.ProductCategories.Remove(category);
                    await db.SaveChangesAsync(ct);
                    return Results.NoContent();
                }, logger, "deleting a category"))
            .WithName("DeleteCategory");
    }

    // ── Shared with the product/material endpoints ────────────────────────────

    /// <summary>Trimmed name, or null when blank. Categories are matched case-insensitively but stored as typed.</summary>
    internal static string? Clean(string? name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    internal static Task<bool> ExistsAsync(WorkitDbContext db, Guid companyId, string name, Guid? exceptId, CancellationToken ct) =>
        db.ProductCategories.AnyAsync(c => c.CompanyId == companyId && c.Id != exceptId && c.Name.ToLower() == name.ToLower(), ct);

    /// <summary>The stored spelling of a category the owner referred to, or null when there is no such category.</summary>
    internal static async Task<string?> ResolveAsync(WorkitDbContext db, Guid companyId, string? name, CancellationToken ct)
    {
        var clean = Clean(name);
        if (clean is null) return null;
        return await db.ProductCategories.Where(c => c.CompanyId == companyId && c.Name.ToLower() == clean.ToLower()).Select(c => c.Name).FirstOrDefaultAsync(ct);
    }

    /// <summary>The category by name, created if the company does not have it yet (for accepted suggestions).</summary>
    internal static async Task<string> ResolveOrCreateAsync(WorkitDbContext db, Guid companyId, string name, CancellationToken ct)
    {
        var existing = await ResolveAsync(db, companyId, name, ct);
        if (existing is not null) return existing;
        var clean = Clean(name)!;
        var order = await db.ProductCategories.Where(c => c.CompanyId == companyId).Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? 0;
        db.ProductCategories.Add(new ProductCategory { CompanyId = companyId, Name = clean, SortOrder = order + 1 });
        await db.SaveChangesAsync(ct);
        return clean;
    }

    /// <summary>Names already on products/materials become categories, once, so nothing that was categorised goes missing.</summary>
    internal static async Task EnsureSeededAsync(WorkitDbContext db, Guid companyId, CancellationToken ct)
    {
        var known = (await db.ProductCategories.Where(c => c.CompanyId == companyId).Select(c => c.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inUse = (await db.PaydayProducts.Where(p => p.CompanyId == companyId && p.Category != "").Select(p => p.Category).Distinct().ToListAsync(ct))
            .Concat(await db.Materials.Where(m => m.CompanyId == companyId && m.Category != "").Select(m => m.Category).Distinct().ToListAsync(ct))
            .Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
        var order = await db.ProductCategories.Where(c => c.CompanyId == companyId).Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? 0;
        var added = false;
        foreach (var name in inUse.Where(n => !known.Contains(n)))
        {
            db.ProductCategories.Add(new ProductCategory { CompanyId = companyId, Name = name, SortOrder = ++order });
            added = true;
        }
        if (added) await db.SaveChangesAsync(ct);
    }

    internal static async Task<List<ProductCategoryRow>> ListAsync(WorkitDbContext db, Guid companyId, CancellationToken ct)
    {
        var categories = await db.ProductCategories.Where(c => c.CompanyId == companyId).OrderBy(c => c.Name).ToListAsync(ct);
        var products  = await db.PaydayProducts.Where(p => p.CompanyId == companyId && !p.Archived && p.Category != "").GroupBy(p => p.Category).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var materials = await db.Materials.Where(m => m.CompanyId == companyId && m.IsActive && m.Category != "").GroupBy(m => m.Category).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return categories.Select(c => new ProductCategoryRow(c.Id, c.Name, c.SortOrder, products.GetValueOrDefault(c.Name), materials.GetValueOrDefault(c.Name))).ToList();
    }

    private static async Task<(int Products, int Materials)> RenameAsync(WorkitDbContext db, Guid companyId, ProductCategory category, string newName, CancellationToken ct)
    {
        var oldName = category.Name;
        category.Name = newName;
        var products  = await db.PaydayProducts.Where(p => p.CompanyId == companyId && p.Category == oldName).ToListAsync(ct);
        var materials = await db.Materials.Where(m => m.CompanyId == companyId && m.Category == oldName).ToListAsync(ct);
        foreach (var p in products) p.Category = newName;
        foreach (var m in materials) m.Category = newName;
        await db.SaveChangesAsync(ct);
        return (products.Count, materials.Count);
    }

    private static async Task Reassign(WorkitDbContext db, Guid companyId, string from, string to, CancellationToken ct)
    {
        foreach (var p in await db.PaydayProducts.Where(p => p.CompanyId == companyId && p.Category == from).ToListAsync(ct)) p.Category = to;
        foreach (var m in await db.Materials.Where(m => m.CompanyId == companyId && m.Category == from).ToListAsync(ct)) m.Category = to;
    }

    private static string Describe(int products, int materials)
    {
        var parts = new List<string>();
        if (products > 0) parts.Add($"{products} product{(products == 1 ? "" : "s")}");
        if (materials > 0) parts.Add($"{materials} material{(materials == 1 ? "" : "s")}");
        return string.Join(" and ", parts);
    }
}
