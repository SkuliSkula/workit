using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Auth;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal static class ToolEndpoints
{
    internal static void MapToolEndpoints(this WebApplication app)
    {
        var securedApi = app.MapGroup("/api").RequireAuthorization().WithTags("Tools");
        var logger = app.Logger;

        securedApi.MapGet("/tools", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();
                    var tools = await db.Tools
                        .Where(x => x.CompanyId == userContext.CompanyId)
                        .OrderBy(x => x.Name)
                        .ToListAsync(ct);
                    return Results.Ok(tools);
                },
                logger,
                "loading tools"))
            .WithName("GetTools");

        securedApi.MapPost("/tools", async (WorkitDbContext db, HttpContext httpContext, Tool tool, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                    {
                        return Results.Forbid();
                    }

                    if (string.IsNullOrWhiteSpace(tool.Name))
                    {
                        return Results.BadRequest("Tool name is required.");
                    }

                    var userContext = httpContext.User.ToUserContext();
                    tool.CompanyId = userContext.CompanyId;
                    tool.Name = tool.Name.Trim();
                    tool.Description = tool.Description.Trim();
                    tool.SerialNumber = tool.SerialNumber.Trim();

                    db.Tools.Add(tool);
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/tools/{tool.Id}", tool);
                },
                logger,
                "creating a tool"))
            .WithName("CreateTool");

        securedApi.MapPut("/tools/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, Tool tool, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                    {
                        return Results.Forbid();
                    }

                    if (id != tool.Id)
                    {
                        return Results.BadRequest("Tool id mismatch.");
                    }

                    if (string.IsNullOrWhiteSpace(tool.Name))
                    {
                        return Results.BadRequest("Tool name is required.");
                    }

                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.Tools.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (existing is null)
                    {
                        return Results.NotFound();
                    }

                    existing.Name = tool.Name.Trim();
                    existing.Description = tool.Description.Trim();
                    existing.SerialNumber = tool.SerialNumber.Trim();

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                },
                logger,
                "updating a tool"))
            .WithName("UpdateTool");

        securedApi.MapDelete("/tools/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                    {
                        return Results.Forbid();
                    }

                    var userContext = httpContext.User.ToUserContext();
                    var existing = await db.Tools.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (existing is null)
                    {
                        return Results.NotFound();
                    }

                    // Check if tool is currently assigned
                    var isAssigned = await db.ToolAssignments
                        .AnyAsync(x => x.ToolId == id && x.ReturnedAt == null, ct);
                    if (isAssigned)
                    {
                        return Results.BadRequest("Cannot delete a tool that is currently assigned to an employee.");
                    }

                    db.Tools.Remove(existing);
                    await db.SaveChangesAsync(ct);
                    return Results.NoContent();
                },
                logger,
                "deleting a tool"))
            .WithName("DeleteTool");

        // ── Tool Assignments ───────────────────────────────────────────────────

        securedApi.MapGet("/tools/assignments", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();

                    IQueryable<ToolAssignment> query = db.ToolAssignments
                        .Where(x => x.CompanyId == userContext.CompanyId);

                    if (httpContext.User.IsOwnerOrAdmin())
                    {
                        // Owner sees all assignments (full history)
                        query = query.OrderByDescending(x => x.AssignedAt);
                    }
                    else if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal))
                    {
                        // Employee sees only active (unreturned) assignments so they can see who has what
                        query = query.Where(x => x.ReturnedAt == null).OrderBy(x => x.ToolId);
                    }
                    else
                    {
                        return Results.Forbid();
                    }

                    var assignments = await query.ToListAsync(ct);
                    return Results.Ok(assignments);
                },
                logger,
                "loading tool assignments"))
            .WithName("GetToolAssignments");

        securedApi.MapPost("/tools/{id:guid}/assign", async (WorkitDbContext db, HttpContext httpContext, Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();

                    if (userContext.EmployeeId is not Guid currentEmployeeId)
                    {
                        return Results.Forbid();
                    }

                    var tool = await db.Tools.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
                    if (tool is null)
                    {
                        return Results.NotFound();
                    }

                    // Check tool is not already assigned
                    var alreadyAssigned = await db.ToolAssignments
                        .AnyAsync(x => x.ToolId == id && x.ReturnedAt == null, ct);
                    if (alreadyAssigned)
                    {
                        return Results.BadRequest("This tool is already assigned to another employee.");
                    }

                    var assignment = new ToolAssignment
                    {
                        ToolId = id,
                        CompanyId = userContext.CompanyId,
                        EmployeeId = currentEmployeeId,
                        AssignedAt = DateTimeOffset.UtcNow
                    };

                    db.ToolAssignments.Add(assignment);
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/tools/{id}/assignments", assignment);
                },
                logger,
                "assigning a tool"))
            .WithName("AssignTool");

        securedApi.MapPost("/tools/{id:guid}/return", async (WorkitDbContext db, HttpContext httpContext, Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();

                    if (userContext.EmployeeId is not Guid currentEmployeeId)
                    {
                        // Owners can return tools too
                        if (!httpContext.User.IsOwnerOrAdmin())
                        {
                            return Results.Forbid();
                        }

                        // Owner: return current assignment regardless of employee
                        var ownerAssignment = await db.ToolAssignments
                            .FirstOrDefaultAsync(x => x.ToolId == id && x.CompanyId == userContext.CompanyId && x.ReturnedAt == null, ct);
                        if (ownerAssignment is null)
                        {
                            return Results.NotFound();
                        }

                        ownerAssignment.ReturnedAt = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(ct);
                        return Results.Ok(ownerAssignment);
                    }

                    var assignment = await db.ToolAssignments
                        .FirstOrDefaultAsync(x => x.ToolId == id && x.EmployeeId == currentEmployeeId && x.ReturnedAt == null, ct);
                    if (assignment is null)
                    {
                        return Results.NotFound();
                    }

                    assignment.ReturnedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                    return Results.Ok(assignment);
                },
                logger,
                "returning a tool"))
            .WithName("ReturnTool");
    }
}
