using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Api;
using Workit.Shared.Auth;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal static class MaterialEndpoints
{
    internal static void MapMaterialEndpoints(this WebApplication app)
    {
        var securedApi = app.MapGroup("/api").RequireAuthorization().WithTags("Materials");
        var logger = app.Logger;

        securedApi.MapGet("/materials", async (WorkitDbContext db, HttpContext httpContext, string? category, bool? activeOnly, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();
                    var query = db.Materials.Where(x => x.CompanyId == userContext.CompanyId);

                    if (!string.IsNullOrWhiteSpace(category))
                        query = query.Where(x => x.Category == category);

                    if (activeOnly == true)
                        query = query.Where(x => x.IsActive);

                    var materials = await query.OrderBy(x => x.Category).ThenBy(x => x.Name).ToListAsync(ct);
                    return Results.Ok(materials);
                },
                logger,
                "loading materials"))
            .WithName("GetMaterials");

        securedApi.MapPost("/materials", async (WorkitDbContext db, HttpContext httpContext, Material material, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                        return Results.Forbid();

                    if (string.IsNullOrWhiteSpace(material.Name))
                        return Results.BadRequest("Material name is required.");

                    var userContext = httpContext.User.ToUserContext();
                    material.CompanyId   = userContext.CompanyId;
                    material.Name        = material.Name.Trim();
                    material.ProductCode = material.ProductCode.Trim();
                    material.Category    = material.Category.Trim();
                    material.Description = material.Description.Trim();
                    material.Unit        = material.Unit.Trim();

                    db.Materials.Add(material);
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/materials/{material.Id}", material);
                },
                logger,
                "creating a material"))
            .WithName("CreateMaterial");

        securedApi.MapPut("/materials/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, Material material, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                        return Results.Forbid();

                    if (id != material.Id)
                        return Results.BadRequest("Material id mismatch.");

                    if (string.IsNullOrWhiteSpace(material.Name))
                        return Results.BadRequest("Material name is required.");

                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.Materials.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (existing is null)
                        return Results.NotFound();

                    existing.Name          = material.Name.Trim();
                    existing.ProductCode   = material.ProductCode.Trim();
                    existing.Category      = material.Category.Trim();
                    existing.Unit          = material.Unit.Trim();
                    existing.PurchasePrice = material.PurchasePrice;
                    existing.MarkupFactor  = material.MarkupFactor > 0 ? material.MarkupFactor : 1.5m;
                    existing.UnitPrice     = material.UnitPrice;
                    existing.VatRate       = material.VatRate;
                    existing.Quantity      = material.Quantity;
                    existing.Description   = material.Description.Trim();
                    existing.IsActive      = material.IsActive;

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                },
                logger,
                "updating a material"))
            .WithName("UpdateMaterial");

        securedApi.MapDelete("/materials/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                        return Results.Forbid();

                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.Materials.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (existing is null)
                        return Results.NotFound();

                    db.Materials.Remove(existing);
                    await db.SaveChangesAsync(ct);
                    return Results.NoContent();
                },
                logger,
                "deleting a material"))
            .WithName("DeleteMaterial");

        // ── Material Usage ─────────────────────────────────────────────────────

        securedApi.MapGet("/materials/usage", async (WorkitDbContext db, HttpContext httpContext, Guid? jobId, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();
                    var query = db.MaterialUsages.Where(x => x.CompanyId == userContext.CompanyId);

                    if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal))
                    {
                        if (userContext.EmployeeId is not Guid currentEmployeeId)
                            return Results.Forbid();
                        query = query.Where(x => x.EmployeeId == currentEmployeeId);
                    }

                    if (jobId is not null)
                        query = query.Where(x => x.JobId == jobId);

                    var usage = await query.OrderByDescending(x => x.UsedAt).ToListAsync(ct);
                    return Results.Ok(usage);
                },
                logger,
                "loading material usage"))
            .WithName("GetMaterialUsage");

        securedApi.MapPost("/materials/usage", async (WorkitDbContext db, HttpContext httpContext, MaterialUsage usage, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();

                    if (string.Equals(userContext.Role, WorkitRoles.Owner, StringComparison.Ordinal)
                        || string.Equals(userContext.Role, WorkitRoles.Admin, StringComparison.Ordinal))
                    {
                        usage.CompanyId = userContext.CompanyId;
                        var employeeExists = await db.Employees.AnyAsync(x => x.Id == usage.EmployeeId && x.CompanyId == userContext.CompanyId, ct);
                        if (!employeeExists)
                            return Results.BadRequest("Employee not found in this company.");
                    }
                    else if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal) &&
                             userContext.EmployeeId is Guid currentEmployeeId)
                    {
                        usage.CompanyId  = userContext.CompanyId;
                        usage.EmployeeId = currentEmployeeId;
                    }
                    else
                    {
                        return Results.Forbid();
                    }

                    var material = await db.Materials.FirstOrDefaultAsync(x => x.Id == usage.MaterialId && x.CompanyId == userContext.CompanyId, ct);
                    if (material is null)
                        return Results.BadRequest("Material not found.");

                    if (usage.Quantity <= 0)
                        return Results.BadRequest("Quantity must be greater than zero.");

                    // Deduct from stock
                    material.Quantity = Math.Max(0, material.Quantity - usage.Quantity);

                    usage.UsedAt = DateTimeOffset.UtcNow;

                    db.MaterialUsages.Add(usage);
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/materials/usage/{usage.Id}", usage);
                },
                logger,
                "logging material usage"))
            .WithName("CreateMaterialUsage");

        securedApi.MapPost("/materials/usage/mark-invoiced", async (WorkitDbContext db, HttpContext httpContext, MarkInvoicedRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                        return Results.Forbid();

                    var userContext = httpContext.User.ToUserContext();

                    if (request.Ids is null || request.Ids.Count == 0)
                        return Results.BadRequest("No material usage ids provided.");

                    var usages = await db.MaterialUsages
                        .Where(x => x.CompanyId == userContext.CompanyId && request.Ids.Contains(x.Id))
                        .ToListAsync(ct);

                    var now = DateTimeOffset.UtcNow;
                    foreach (var u in usages)
                    {
                        u.IsInvoiced = true;
                        u.InvoicedAt = now;
                        u.PaydayInvoiceNumber = request.PaydayInvoiceNumber;
                    }

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(new { Marked = usages.Count });
                },
                logger,
                "marking material usage as invoiced"))
            .WithName("MarkMaterialUsageInvoiced");

        securedApi.MapPost("/materials/usage/mark-uninvoiced", async (WorkitDbContext db, HttpContext httpContext, MarkUninvoicedRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                        return Results.Forbid();

                    var userContext = httpContext.User.ToUserContext();

                    var usages = await db.MaterialUsages
                        .Where(x => x.CompanyId == userContext.CompanyId
                                  && x.IsInvoiced
                                  && x.PaydayInvoiceNumber == request.PaydayInvoiceNumber)
                        .ToListAsync(ct);

                    foreach (var u in usages)
                    {
                        u.IsInvoiced = false;
                        u.InvoicedAt = null;
                        u.PaydayInvoiceNumber = null;
                    }

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(new { Reset = usages.Count });
                },
                logger,
                "unmarking material usage as invoiced"))
            .WithName("MarkMaterialUsageUninvoiced");
    }
}
