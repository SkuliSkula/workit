using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Services;
using Workit.Shared.Api;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

/// <summary>
/// Upload, list, download and delete files (photos and PDFs) attached to a job.
/// Any member of the job's company may upload and view; a file may be deleted by
/// its uploader or by an Owner/Admin. Bytes live in object storage, reachable only
/// through the download endpoint — the storage key is never returned to clients.
/// </summary>
internal static class JobAttachmentEndpoints
{
    private const long MaxFileSizeBytes = 25 * 1024 * 1024; // 25 MB

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/heic", "image/heif", "image/webp", "image/gif", "application/pdf"
    };

    internal static void MapJobAttachmentEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/jobs/{jobId:guid}/attachments")
            .RequireAuthorization()
            .WithTags("Job Attachments");
        var logger = app.Logger;

        // ── List ────────────────────────────────────────────────────────────────
        api.MapGet("", async (WorkitDbContext db, HttpContext httpContext, Guid jobId, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();
                    if (!await JobInCompanyAsync(db, jobId, userContext.CompanyId, ct))
                        return Results.NotFound();

                    var items = await db.JobAttachments
                        .AsNoTracking()
                        .Where(a => a.JobId == jobId && a.CompanyId == userContext.CompanyId)
                        .OrderByDescending(a => a.UploadedAt)
                        .Select(a => ToInfo(a))
                        .ToListAsync(ct);

                    return Results.Ok(items);
                }, logger, "listing job attachments"))
            .WithName("GetJobAttachments");

        // ── Upload ──────────────────────────────────────────────────────────────
        api.MapPost("", async (WorkitDbContext db, IFileStorageService storage, HttpContext httpContext, Guid jobId, IFormFile file, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();
                    if (!await JobInCompanyAsync(db, jobId, userContext.CompanyId, ct))
                        return Results.NotFound();

                    if (file is null || file.Length == 0)
                        return Results.BadRequest("No file was provided.");

                    if (file.Length > MaxFileSizeBytes)
                        return Results.BadRequest($"File exceeds the {MaxFileSizeBytes / (1024 * 1024)} MB limit.");

                    var contentType = file.ContentType?.Trim() ?? string.Empty;
                    if (!AllowedContentTypes.Contains(contentType))
                        return Results.BadRequest("Only images and PDF files are allowed.");

                    var attachmentId = Guid.NewGuid();
                    var ext = SafeExtension(file.FileName);
                    var storageKey = $"{userContext.CompanyId}/{jobId}/{attachmentId}{ext}";

                    await using (var stream = file.OpenReadStream())
                    {
                        await storage.SaveAsync(storageKey, stream, contentType, ct);
                    }

                    var uploaderName = await ResolveUploaderNameAsync(db, httpContext, userContext, ct);

                    var attachment = new JobAttachment
                    {
                        Id               = attachmentId,
                        CompanyId        = userContext.CompanyId,
                        JobId            = jobId,
                        FileName         = SafeFileName(file.FileName),
                        ContentType      = contentType,
                        SizeBytes        = file.Length,
                        Kind             = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                                               ? AttachmentKind.Image
                                               : AttachmentKind.Document,
                        StorageKey       = storageKey,
                        UploadedByUserId = userContext.UserId,
                        UploadedByName   = uploaderName,
                        UploadedAt       = DateTimeOffset.UtcNow,
                    };

                    db.JobAttachments.Add(attachment);
                    await db.SaveChangesAsync(ct);

                    return Results.Created($"/api/jobs/{jobId}/attachments/{attachment.Id}", ToInfo(attachment));
                }, logger, "uploading a job attachment"))
            .DisableAntiforgery()
            .WithName("UploadJobAttachment");

        // ── Download ──────────────────────────────────────────────────────────────
        api.MapGet("/{id:guid}", async (WorkitDbContext db, IFileStorageService storage, HttpContext httpContext, Guid jobId, Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();
                    var attachment = await db.JobAttachments
                        .AsNoTracking()
                        .FirstOrDefaultAsync(a => a.Id == id && a.JobId == jobId && a.CompanyId == userContext.CompanyId, ct);
                    if (attachment is null)
                        return Results.NotFound();

                    var stored = await storage.GetAsync(attachment.StorageKey, ct);
                    if (stored is null)
                    {
                        logger.LogWarning("Attachment {Id} metadata exists but the stored file is missing.", id);
                        return Results.NotFound();
                    }

                    return Results.File(stored.Content, attachment.ContentType, fileDownloadName: attachment.FileName);
                }, logger, "downloading a job attachment"))
            .WithName("DownloadJobAttachment");

        // ── Delete ──────────────────────────────────────────────────────────────
        api.MapDelete("/{id:guid}", async (WorkitDbContext db, IFileStorageService storage, HttpContext httpContext, Guid jobId, Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();
                    var attachment = await db.JobAttachments
                        .FirstOrDefaultAsync(a => a.Id == id && a.JobId == jobId && a.CompanyId == userContext.CompanyId, ct);
                    if (attachment is null)
                        return Results.NotFound();

                    // Uploader or Owner/Admin may delete.
                    if (attachment.UploadedByUserId != userContext.UserId && !httpContext.User.IsOwnerOrAdmin())
                        return Results.Forbid();

                    db.JobAttachments.Remove(attachment);
                    await db.SaveChangesAsync(ct);

                    // Best-effort blob cleanup; the row is already gone.
                    try
                    {
                        await storage.DeleteAsync(attachment.StorageKey, ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to delete stored file {Key} for attachment {Id}.", attachment.StorageKey, id);
                    }

                    return Results.NoContent();
                }, logger, "deleting a job attachment"))
            .WithName("DeleteJobAttachment");
    }

    private static Task<bool> JobInCompanyAsync(WorkitDbContext db, Guid jobId, Guid companyId, CancellationToken ct) =>
        db.Jobs.AnyAsync(j => j.Id == jobId && j.CompanyId == companyId, ct);

    private static async Task<string> ResolveUploaderNameAsync(
        WorkitDbContext db, HttpContext httpContext, UserContext userContext, CancellationToken ct)
    {
        var name = await db.AppUsers
            .Where(u => u.Id == userContext.UserId)
            .Select(u => u.Name)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        // Employee AppUsers have no Name; use their Employee display name.
        if (userContext.EmployeeId is Guid employeeId)
        {
            var displayName = await db.Employees
                .Where(e => e.Id == employeeId)
                .Select(e => e.DisplayName)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;
        }

        var email = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        return string.IsNullOrWhiteSpace(email) ? "Unknown" : email;
    }

    private static JobAttachmentInfo ToInfo(JobAttachment a) => new()
    {
        Id               = a.Id,
        JobId            = a.JobId,
        FileName         = a.FileName,
        ContentType      = a.ContentType,
        SizeBytes        = a.SizeBytes,
        Kind             = a.Kind,
        UploadedByUserId = a.UploadedByUserId,
        UploadedByName   = a.UploadedByName,
        UploadedAt       = a.UploadedAt,
    };

    /// <summary>Strips any path components a client may have sent in the file name.</summary>
    private static string SafeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "file";
        var name = Path.GetFileName(fileName.Trim());
        return string.IsNullOrWhiteSpace(name) ? "file" : name;
    }

    private static string SafeExtension(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty);
        // Keep only a short, alphanumeric extension; ignore anything unusual.
        return ext.Length is > 1 and <= 6 && ext[1..].All(char.IsLetterOrDigit)
            ? ext.ToLowerInvariant()
            : string.Empty;
    }
}
