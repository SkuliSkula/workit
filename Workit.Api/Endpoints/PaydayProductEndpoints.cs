using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
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
        app.MapPost("/api/payday/products/sync", async (WorkitDbContext db, HttpContext http, IPaydayProductsApi payday, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var user = http.User.ToUserContext();
                    var outcome = await SyncAsync(db, payday, user.CompanyId, ct);
                    return outcome.Error is null
                        ? Results.Ok(outcome.Result)
                        : Results.Content(outcome.Error, "text/plain", statusCode: StatusCodes.Status502BadGateway);
                }, logger, "syncing Payday products"))
            .RequireAuthorization()
            .AddEndpointFilter<PaydayCredentialsFilter>()
            .WithTags("Payday products")
            .WithName("SyncPaydayProducts");
    }

    internal sealed record SyncOutcome(PaydayProductSyncResult? Result, string? Error);

    /// <summary>
    /// Pulls every page of the company's Payday products and upserts the cache.
    /// Products Payday no longer returns are marked archived, never deleted —
    /// something may still point at them. Role, unit and category are kept.
    /// A brand-new stock-tracked product defaults to Material.
    /// </summary>
    internal static async Task<SyncOutcome> SyncAsync(WorkitDbContext db, IPaydayProductsApi payday, Guid companyId, CancellationToken ct)
    {
        var fetched = new List<PaydayProduct>();
        for (var page = 1; ; page++)
        {
            var result = await payday.GetAllAsync(page: page, perPage: 100);
            if (!result.IsSuccess || result.Value is null)
                return new SyncOutcome(null, result.ErrorMessage ?? "Payday products could not be loaded right now.");
            fetched.AddRange(result.Value.Products);
            if (page >= result.Value.Pages || result.Value.Products.Count == 0) break;
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await db.PaydayProducts.Where(p => p.CompanyId == companyId).ToListAsync(ct);
        var byPaydayId = existing.ToDictionary(p => p.PaydayId);
        int added = 0, updated = 0, archived = 0;

        foreach (var remote in fetched)
        {
            if (byPaydayId.TryGetValue(remote.Id, out var local))
            {
                Apply(local, remote, now);
                updated++;
            }
            else
            {
                local = new PaydayProductCache { CompanyId = companyId, PaydayId = remote.Id };
                Apply(local, remote, now);
                local.Role = remote.Quantity is not null ? PaydayProductRole.Material : PaydayProductRole.Unassigned;
                db.PaydayProducts.Add(local);
                added++;
            }
        }

        var remoteIds = fetched.Select(r => r.Id).ToHashSet();
        foreach (var gone in existing.Where(p => !remoteIds.Contains(p.PaydayId) && !p.Archived))
        {
            gone.Archived = true;
            gone.SyncedAt = now;
            archived++;
        }

        await db.SaveChangesAsync(ct);
        return new SyncOutcome(new PaydayProductSyncResult(fetched.Count, added, updated, archived, now), null);
    }

    private static void Apply(PaydayProductCache local, PaydayProduct remote, DateTimeOffset now)
    {
        local.Sku                  = remote.Sku?.Trim() ?? string.Empty;
        local.Name                 = remote.Name?.Trim() ?? string.Empty;
        local.Description          = remote.Description?.Trim() ?? string.Empty;
        local.SalePriceExVat       = remote.SalesUnitPriceExcludingVAT ?? 0m;
        local.SalePriceIncVat      = remote.SalesUnitPriceIncludingVAT ?? 0m;
        local.VatPercentage        = remote.VatPercentage ?? 0m;
        local.SalesLedgerAccountId = remote.SalesLedgerAccountId;
        local.Quantity             = remote.Quantity;
        local.Archived             = remote.Archived;
        local.Tags                 = remote.Tags is { Count: > 0 } ? string.Join(",", remote.Tags) : string.Empty;
        local.SyncedAt             = now;
    }
}
