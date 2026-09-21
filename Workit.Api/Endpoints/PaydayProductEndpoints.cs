using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Payday;
using Workit.Shared.Models;
using Workit.Shared.Payday;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

/// <summary>
/// Payday's product list as Workit's price list. The cache is what every screen
/// reads; only <c>sync</c> talks to Payday (Owner/Admin, via
/// <see cref="PaydayCredentialsFilter"/>). Roles, unit and category are
/// Workit's own and survive a sync.
/// </summary>
internal static class PaydayProductEndpoints
{
    internal static void MapPaydayProductEndpoints(this WebApplication app)
    {
        var logger = app.Logger;

        // Reads come from the cache and need no Payday credentials, so employees'
        // material pickers keep working when Payday is down or not connected.
        var cache = app.MapGroup("/api/payday/products").RequireAuthorization().WithTags("Payday products");

        cache.MapGet("/", async (WorkitDbContext db, HttpContext http, CancellationToken ct, bool includeArchived = false) =>
                await ExecuteDbAsync(async () =>
                {
                    var user = http.User.ToUserContext();
                    var items = await db.PaydayProducts
                        .Where(p => p.CompanyId == user.CompanyId && (includeArchived || !p.Archived))
                        .OrderBy(p => p.Sku)
                        .ToListAsync(ct);
                    return Results.Ok(items);
                }, logger, "loading Payday products"))
            .WithName("GetPaydayProducts");

        // The console's list: search, role filter, sort and paging happen in SQL so a
        // company with a thousand products gets fifty rows, not a thousand editors.
        cache.MapGet("/page", async (WorkitDbContext db, HttpContext http, CancellationToken ct,
                string? q = null, PaydayProductRole? role = null, bool includeArchived = false,
                string sort = "sku", string dir = "asc", int page = 1, int pageSize = 50) =>
                await ExecuteDbAsync(async () =>
                {
                    var user = http.User.ToUserContext();
                    var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
                    return Results.Ok(await QueryPageAsync(db, user.CompanyId, q, role, includeArchived, sort, desc, page, pageSize, ct));
                }, logger, "loading a page of Payday products"))
            .WithName("GetPaydayProductsPage");

        cache.MapPut("/{id:guid}/role", async (WorkitDbContext db, HttpContext http, Guid id, PaydayProductRoleUpdate body, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!http.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var user = http.User.ToUserContext();
                    var product = await db.PaydayProducts.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == user.CompanyId, ct);
                    if (product is null) return Results.NotFound();

                    product.Role     = body.Role;
                    product.Unit     = body.Unit?.Trim() ?? product.Unit;
                    product.Category = body.Category?.Trim() ?? product.Category;
                    await db.SaveChangesAsync(ct);
                    return Results.Ok(product);
                }, logger, "updating a Payday product role"))
            .WithName("SetPaydayProductRole");

        // The one write to Payday-facing state; the filter loads the company's credentials.
        app.MapPost("/api/payday/products/sync", async (HttpContext http, PaydayProductSyncService sync, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var user = http.User.ToUserContext();
                    var outcome = await sync.SyncAsync(user.CompanyId, ct);
                    return outcome.Error is null
                        ? Results.Ok(outcome.Result)
                        : Results.Content(outcome.Error, "text/plain", statusCode: StatusCodes.Status502BadGateway);
                }, logger, "syncing Payday products"))
            .RequireAuthorization()
            .AddEndpointFilter<PaydayCredentialsFilter>()
            .WithTags("Payday products")
            .WithName("SyncPaydayProducts");

        // Payday owns the price list, so a product made in Workit is made in Payday first and
        // then pulled into the cache — with the role the owner picked, so it is usable at once.
        app.MapGet("/api/payday/products/ledger-accounts", async (IPaydayProductsApi payday) =>
            {
                var accounts = await payday.GetSalesLedgerAccountsAsync();
                return accounts.IsSuccess
                    ? Results.Ok((accounts.Value ?? []).Where(a => !a.Archived).OrderBy(a => a.Code).ToList())
                    : Results.Content(accounts.ErrorMessage ?? "Payday's sales accounts could not be loaded.", "text/plain", statusCode: StatusCodes.Status502BadGateway);
            })
            .RequireAuthorization()
            .AddEndpointFilter<PaydayCredentialsFilter>()
            .WithTags("Payday products")
            .WithName("GetPaydaySalesLedgerAccounts");

        app.MapPost("/api/payday/products", async (HttpContext http, WorkitDbContext db, IPaydayProductsApi payday, PaydayProductSyncService sync, PaydayProductCreate body, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var user = http.User.ToUserContext();
                    var name = body.Name?.Trim() ?? "";
                    var sku  = body.Sku?.Trim() ?? "";
                    if (name.Length == 0 || sku.Length == 0) return Results.BadRequest("Name and SKU are required.");
                    if (body.SalePriceExVat < 0) return Results.BadRequest("The sale price cannot be negative.");
                    if (body.SalesLedgerAccountId == Guid.Empty) return Results.BadRequest("Pick the sales account the product is booked to.");
                    if (await db.PaydayProducts.AnyAsync(p => p.CompanyId == user.CompanyId && !p.Archived && p.Sku == sku, ct))
                        return Results.Conflict($"A product with SKU {sku} already exists in Payday.");

                    var created = await payday.CreateAsync(new CreateProductRequest
                    {
                        Name = name, Sku = sku, Description = body.Description?.Trim(),
                        Quantity = body.TrackStock ? (body.OpeningQuantity ?? 0) : null,
                        SalesUnitPriceExcludingVAT = body.SalePriceExVat, VatPercentage = body.VatPercentage,
                        SalesLedgerAccountId = body.SalesLedgerAccountId,
                        PurchaseDate = body.TrackStock && body.OpeningQuantity > 0 ? DateTime.Today.ToString("yyyy-MM-dd") : null,
                        PurchaseUnitPriceExcludingVAT = body.TrackStock && body.OpeningQuantity > 0 && body.PurchasePriceExVat > 0 ? body.PurchasePriceExVat : null,
                        Tags = ["workit"],
                    });
                    if (!created.IsSuccess || created.Value is null)
                        return Results.Content(created.ErrorMessage ?? "Payday refused to create the product.", "text/plain", statusCode: StatusCodes.Status502BadGateway);

                    // Pull it into the cache (and mirror to Materials where switched on), then stamp the role.
                    var outcome = await sync.SyncAsync(user.CompanyId, ct);
                    var cached = await db.PaydayProducts.FirstOrDefaultAsync(p => p.CompanyId == user.CompanyId && p.PaydayId == created.Value.Id, ct);
                    if (cached is null)
                        return Results.Content(outcome.Error ?? "The product was created in Payday but has not appeared in the list yet — press Sync now.", "text/plain", statusCode: StatusCodes.Status502BadGateway);

                    cached.Role     = body.Role;
                    cached.Unit     = body.Unit?.Trim() ?? cached.Unit;
                    cached.Category = body.Category?.Trim() ?? cached.Category;
                    await db.SaveChangesAsync(ct);
                    if (body.Role == PaydayProductRole.Material)
                    {
                        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == user.CompanyId, ct);
                        if (company?.MaterialsManagedInPayday == true)
                            await sync.MirrorMaterialsAsync(user.CompanyId, await db.PaydayProducts.Where(p => p.CompanyId == user.CompanyId).ToListAsync(ct), ct);
                    }
                    return Results.Created($"/api/payday/products/{cached.Id}", cached);
                }, logger, "creating a Payday product"))
            .RequireAuthorization()
            .AddEndpointFilter<PaydayCredentialsFilter>()
            .WithTags("Payday products")
            .WithName("CreatePaydayProduct");

        // Phase 4 switch-over: link/create products for every material, then manage materials in Payday.
        app.MapPost("/api/payday/materials/migrate", async (HttpContext http, MaterialsMigrationService migration, CancellationToken ct, bool dryRun = true) =>
                await ExecuteDbAsync(async () =>
                {
                    var user = http.User.ToUserContext();
                    var result = await migration.RunAsync(user.CompanyId, dryRun, ct);
                    return result.Error is null
                        ? Results.Ok(result)
                        : Results.Content(result.Error, "text/plain", statusCode: StatusCodes.Status502BadGateway);
                }, logger, "migrating materials to Payday"))
            .RequireAuthorization()
            .AddEndpointFilter<PaydayCredentialsFilter>()
            .WithTags("Payday products")
            .WithName("MigrateMaterialsToPayday");
    }
    /// <summary>
    /// The console's list query. Search matches SKU, name and category
    /// case-insensitively; role counts are for the live set regardless of the
    /// search so the filter chips stay stable while typing.
    /// </summary>
    internal static async Task<PaydayProductPage> QueryPageAsync(WorkitDbContext db, Guid companyId, string? q, PaydayProductRole? role,
        bool includeArchived, string sort, bool descending, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);

        var live = db.PaydayProducts.Where(p => p.CompanyId == companyId && (includeArchived || !p.Archived));
        var roleCounts = await live.GroupBy(p => p.Role).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var lastSynced = await live.Select(p => (DateTimeOffset?)p.SyncedAt).MaxAsync(ct);

        var query = live;
        if (role is not null) query = query.Where(p => p.Role == role);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            query = query.Where(p => p.Sku.ToLower().Contains(term) || p.Name.ToLower().Contains(term) || p.Category.ToLower().Contains(term));
        }

        query = (sort.ToLowerInvariant(), descending) switch
        {
            ("name", false)  => query.OrderBy(p => p.Name).ThenBy(p => p.Sku),
            ("name", true)   => query.OrderByDescending(p => p.Name).ThenBy(p => p.Sku),
            ("price", false) => query.OrderBy(p => p.SalePriceExVat).ThenBy(p => p.Sku),
            ("price", true)  => query.OrderByDescending(p => p.SalePriceExVat).ThenBy(p => p.Sku),
            // Untracked products sort last either way; they have no stock to compare.
            ("stock", false) => query.OrderBy(p => p.Quantity == null).ThenBy(p => p.Quantity).ThenBy(p => p.Sku),
            ("stock", true)  => query.OrderBy(p => p.Quantity == null).ThenByDescending(p => p.Quantity).ThenBy(p => p.Sku),
            (_, true)        => query.OrderByDescending(p => p.Sku),
            _                => query.OrderBy(p => p.Sku),
        };

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PaydayProductPage(items, total, page, pageSize, roleCounts, lastSynced);
    }
}
