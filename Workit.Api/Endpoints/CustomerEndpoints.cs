using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Payday;
using Workit.Api.Services;
using Workit.Shared.Models;
using Workit.Shared.Payday;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal static class CustomerEndpoints
{
    internal static void MapCustomerEndpoints(this WebApplication app)
    {
        var securedApi = app.MapGroup("/api").RequireAuthorization().WithTags("Customers");
        var logger = app.Logger;

        securedApi.MapGet("/customers", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                    {
                        return Results.Forbid();
                    }

                    var userContext = httpContext.User.ToUserContext();
                    var customers = await db.Customers
                        .Where(x => x.CompanyId == userContext.CompanyId)
                        .OrderBy(x => x.Name)
                        .ToListAsync(ct);
                    return Results.Ok(customers);
                },
                logger,
                "loading customers"))
            .WithName("GetCustomers");

        securedApi.MapPost("/customers", async (WorkitDbContext db, HttpContext httpContext, Customer customer,
                ICredentialProtectionService protection, IPaydayTokenService tokens, PaydayCustomerSyncService payday, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                    {
                        return Results.Forbid();
                    }

                    if (!IsValidCustomer(customer))
                    {
                        return Results.BadRequest("Customer name and SSN are required.");
                    }

                    customer.CompanyId = httpContext.User.ToUserContext().CompanyId;
                    customer.Name = customer.Name.Trim();
                    customer.Ssn = customer.Ssn.Trim();
                    customer.Email = customer.Email.Trim();
                    customer.Phone = customer.Phone.Trim();
                    customer.ContactPerson = customer.ContactPerson.Trim();
                    customer.Address = customer.Address.Trim();
                    customer.ZipCode = customer.ZipCode.Trim();
                    customer.City = customer.City.Trim();
                    customer.Country = customer.Country.Trim();
                    customer.Language = customer.Language.Trim();
                    customer.Comment = customer.Comment.Trim();

                    // A new customer starts unlinked; the write-through below links it.
                    customer.PaydayId          = null;
                    customer.PaydayPushPending = false;
                    customer.PaydayPushError   = null;

                    await customer.StampCreatedAsync(db, httpContext, httpContext.User.ToUserContext(), ct);
                    db.Customers.Add(customer);
                    await WriteThroughAsync(db, protection, tokens, payday, customer, ct);
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/customers/{customer.Id}", customer);
                },
                logger,
                "creating a customer"))
            .WithName("CreateCustomer");

        securedApi.MapPut("/customers/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, Customer customer,
                ICredentialProtectionService protection, IPaydayTokenService tokens, PaydayCustomerSyncService payday, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                    {
                        return Results.Forbid();
                    }

                    if (id != customer.Id)
                    {
                        return Results.BadRequest("Customer id mismatch.");
                    }

                    if (!IsValidCustomer(customer))
                    {
                        return Results.BadRequest("Customer name and SSN are required.");
                    }

                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.Customers.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (existing is null)
                    {
                        return Results.NotFound();
                    }

                    existing.Name = customer.Name.Trim();
                    existing.Ssn = customer.Ssn.Trim();
                    existing.Email = customer.Email.Trim();
                    existing.Phone = customer.Phone.Trim();
                    existing.ContactPerson = customer.ContactPerson.Trim();
                    existing.Address = customer.Address.Trim();
                    existing.ZipCode = customer.ZipCode.Trim();
                    existing.City = customer.City.Trim();
                    existing.Country = customer.Country.Trim();
                    existing.Language = customer.Language.Trim();
                    existing.Comment = customer.Comment.Trim();
                    existing.PlanJobsInTasks = customer.PlanJobsInTasks;
                    // The Payday link is owned by the sync, not the form.

                    await WriteThroughAsync(db, protection, tokens, payday, existing, ct);
                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                },
                logger,
                "updating a customer"))
            .WithName("UpdateCustomer");
    }

    /// <summary>
    /// Workit → Payday on save. Not connected: nothing to do. Connected: push now;
    /// if Payday refuses, the row is flagged pending with Payday's message and the
    /// background sync retries — the owner's save never fails because of Payday.
    /// </summary>
    private static async Task WriteThroughAsync(WorkitDbContext db, ICredentialProtectionService protection, IPaydayTokenService tokens,
        PaydayCustomerSyncService payday, Customer customer, CancellationToken ct)
    {
        if (!await PaydayConnection.TryConnectAsync(db, protection, tokens, customer.CompanyId, ct))
            return;
        await payday.PushAsync(customer, ct);
    }
}
