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
}
