using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Payday;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

/// <summary>
/// The Price tracker page: purchase prices by product and by seller, and the history
/// of the Payday price list. Reads Workit's own stored data only. Owner/Admin.
/// </summary>
internal static class PriceTrackerEndpoints
{
    internal static void MapPriceTrackerEndpoints(this WebApplication app)
    {
        var logger = app.Logger;
        var prices = app.MapGroup("/api/prices").RequireAuthorization().WithTags("Price tracker");

        prices.MapGet("/summary", async (WorkitDbContext db, HttpContext http, CancellationToken ct) =>
            await ExecuteDbAsync(async () => !http.User.IsOwnerOrAdmin() ? Results.Forbid()
                : Results.Ok(await PriceHistory.SummaryAsync(db, http.User.ToUserContext().CompanyId, ct)), logger, "loading the price summary"));

        prices.MapGet("/items", async (WorkitDbContext db, HttpContext http, string? q, string? vendor, CancellationToken ct) =>
            await ExecuteDbAsync(async () => !http.User.IsOwnerOrAdmin() ? Results.Forbid()
                : Results.Ok(await PriceHistory.ItemsAsync(db, http.User.ToUserContext().CompanyId, q, vendor, ct)), logger, "loading tracked items"));

        prices.MapGet("/items/history", async (WorkitDbContext db, HttpContext http, string sku, CancellationToken ct) =>
            await ExecuteDbAsync(async () =>
            {
                if (!http.User.IsOwnerOrAdmin()) return Results.Forbid();
                var history = await PriceHistory.ItemAsync(db, http.User.ToUserContext().CompanyId, sku, ct);
                return history is null ? Results.NotFound() : Results.Ok(history);
            }, logger, "loading an item's price history"));

        prices.MapGet("/vendors", async (WorkitDbContext db, HttpContext http, CancellationToken ct) =>
            await ExecuteDbAsync(async () => !http.User.IsOwnerOrAdmin() ? Results.Forbid()
                : Results.Ok(await PriceHistory.VendorsAsync(db, http.User.ToUserContext().CompanyId, ct)), logger, "loading sellers"));

        prices.MapGet("/changes", async (WorkitDbContext db, HttpContext http, int days = 90, CancellationToken ct = default) =>
            await ExecuteDbAsync(async () => !http.User.IsOwnerOrAdmin() ? Results.Forbid()
                : Results.Ok(await PriceHistory.ChangesAsync(db, http.User.ToUserContext().CompanyId, days, ct)), logger, "loading price-list changes"));
    }
}
