using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Models;
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

        securedApi.MapPost("/customers", async (WorkitDbContext db, HttpContext httpContext, Customer customer, CancellationToken ct) =>
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

                    db.Customers.Add(customer);
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/customers/{customer.Id}", customer);
                },
                logger,
                "creating a customer"))
            .WithName("CreateCustomer");

        securedApi.MapPut("/customers/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, Customer customer, CancellationToken ct) =>
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

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                },
                logger,
                "updating a customer"))
            .WithName("UpdateCustomer");
    }
}
