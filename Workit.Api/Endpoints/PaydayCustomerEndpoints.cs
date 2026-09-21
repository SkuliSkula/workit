using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Payday;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

/// <summary>
/// Customers two-way (Phase 5). <c>sync</c> pushes pending Workit edits and pulls
/// Payday's list; <c>{id}/push</c> retries one customer. Both Owner/Admin via
/// <see cref="PaydayCredentialsFilter"/>. Reads stay on /api/customers.
/// </summary>
internal static class PaydayCustomerEndpoints
{
    internal static void MapPaydayCustomerEndpoints(this WebApplication app)
    {
        var logger = app.Logger;
        var group = app.MapGroup("/api/payday/customers")
            .RequireAuthorization()
            .AddEndpointFilter<PaydayCredentialsFilter>()
            .WithTags("Payday customers");

        group.MapPost("/sync", async (HttpContext http, PaydayCustomerSyncService sync, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var user = http.User.ToUserContext();
                    var outcome = await sync.SyncAsync(user.CompanyId, ct);
                    return outcome.Error is null
                        ? Results.Ok(outcome.Result)
                        : Results.Content(outcome.Error, "text/plain", statusCode: StatusCodes.Status502BadGateway);
                }, logger, "syncing Payday customers"))
            .WithName("SyncPaydayCustomers");

        group.MapPost("/{id:guid}/push", async (HttpContext http, WorkitDbContext db, PaydayCustomerSyncService sync, Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var user = http.User.ToUserContext();
                    var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == user.CompanyId, ct);
                    if (customer is null) return Results.NotFound();

                    var ok = await sync.PushAsync(customer, ct);
                    await db.SaveChangesAsync(ct);
                    return ok
                        ? Results.Ok(customer)
                        : Results.Content(customer.PaydayPushError ?? "Payday did not accept the customer.", "text/plain", statusCode: StatusCodes.Status502BadGateway);
                }, logger, "pushing a customer to Payday"))
            .WithName("PushCustomerToPayday");
    }
}
