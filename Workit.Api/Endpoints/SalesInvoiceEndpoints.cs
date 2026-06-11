using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal static class SalesInvoiceEndpoints
{
    internal static void MapSalesInvoiceEndpoints(this WebApplication app)
    {
        var securedApi = app.MapGroup("/api").RequireAuthorization().WithTags("SalesInvoices");
        var logger = app.Logger;

        // ── List ─────────────────────────────────────────────────────────────
        securedApi.MapGet("/sales-invoices", async (
                WorkitDbContext db, HttpContext httpContext,
                string? status, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    var query = db.Invoices.Where(x => x.CompanyId == companyId);
                    if (!string.IsNullOrWhiteSpace(status))
                        query = query.Where(x => x.Status == status);

                    var invoices = await query
                        .OrderByDescending(x => x.InvoiceDate)
                        .ThenByDescending(x => x.CreatedAt)
                        .ToListAsync(ct);
                    return Results.Ok(invoices);
                }, logger, "loading sales invoices"))
            .WithName("GetSalesInvoices");

        // ── Get by id (with lines + payments) ────────────────────────────────
        securedApi.MapGet("/sales-invoices/{id:guid}", async (
                WorkitDbContext db, HttpContext httpContext,
                Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    var invoice = await db.Invoices
                        .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
                    if (invoice is null) return Results.NotFound();

                    invoice.Lines = await db.InvoiceLines
                        .Where(x => x.InvoiceId == id)
                        .OrderBy(x => x.Position)
                        .ToListAsync(ct);
                    invoice.Payments = await db.InvoicePayments
                        .Where(x => x.InvoiceId == id)
                        .OrderBy(x => x.PaymentDate)
                        .ToListAsync(ct);

                    return Results.Ok(invoice);
                }, logger, "loading sales invoice"))
            .WithName("GetSalesInvoice");

        // ── Create ────────────────────────────────────────────────────────────
        securedApi.MapPost("/sales-invoices", async (
                WorkitDbContext db, HttpContext httpContext,
                Invoice invoice, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    invoice.CompanyId = companyId;
                    invoice.CreatedAt = DateTimeOffset.UtcNow;

                    foreach (var line in invoice.Lines)
                    {
                        line.InvoiceId = invoice.Id;
                        line.CompanyId = companyId;
                    }
                    foreach (var payment in invoice.Payments)
                    {
                        payment.InvoiceId = invoice.Id;
                        payment.CompanyId = companyId;
                    }

                    db.Invoices.Add(invoice);
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/sales-invoices/{invoice.Id}", invoice);
                }, logger, "creating sales invoice"))
            .WithName("CreateSalesInvoice");

        // ── Update header ─────────────────────────────────────────────────────
        securedApi.MapPut("/sales-invoices/{id:guid}", async (
                WorkitDbContext db, HttpContext httpContext,
                Guid id, Invoice invoice, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    if (id != invoice.Id) return Results.BadRequest("Invoice id mismatch.");
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    var existing = await db.Invoices
                        .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
                    if (existing is null) return Results.NotFound();

                    existing.CustomerPaydayId         = invoice.CustomerPaydayId;
                    existing.CustomerSsn              = invoice.CustomerSsn;
                    existing.CustomerName             = invoice.CustomerName;
                    existing.PayorPaydayId            = invoice.PayorPaydayId;
                    existing.PayorSsn                 = invoice.PayorSsn;
                    existing.PayorName                = invoice.PayorName;
                    existing.Number                   = invoice.Number;
                    existing.Status                   = invoice.Status;
                    existing.Description              = invoice.Description;
                    existing.Reference                = invoice.Reference;
                    existing.CurrencyCode             = invoice.CurrencyCode;
                    existing.CurrencyRate             = invoice.CurrencyRate;
                    existing.InvoiceDate              = invoice.InvoiceDate;
                    existing.DueDate                  = invoice.DueDate;
                    existing.FinalDueDate             = invoice.FinalDueDate;
                    existing.SentDate                 = invoice.SentDate;
                    existing.PaidDate                 = invoice.PaidDate;
                    existing.CancelDate               = invoice.CancelDate;
                    existing.CancelledDate            = invoice.CancelledDate;
                    existing.CreditDate               = invoice.CreditDate;
                    existing.RefundDate               = invoice.RefundDate;
                    existing.ClaimCreated             = invoice.ClaimCreated;
                    existing.ClaimFinalDueDate        = invoice.ClaimFinalDueDate;
                    existing.ClaimCancelledDate       = invoice.ClaimCancelledDate;
                    existing.ClaimCancelled           = invoice.ClaimCancelled;
                    existing.AmountExcludingVat       = invoice.AmountExcludingVat;
                    existing.AmountIncludingVat       = invoice.AmountIncludingVat;
                    existing.AmountVat                = invoice.AmountVat;
                    existing.ForeignAmountExcludingVat = invoice.ForeignAmountExcludingVat;
                    existing.ForeignAmountIncludingVat = invoice.ForeignAmountIncludingVat;
                    existing.ForeignAmountVat          = invoice.ForeignAmountVat;
                    existing.VatNumber                = invoice.VatNumber;
                    existing.CreateClaim              = invoice.CreateClaim;
                    existing.CreateElectronicInvoice  = invoice.CreateElectronicInvoice;
                    existing.ElectronicInvoicePartyId = invoice.ElectronicInvoicePartyId;
                    existing.SendEmail                = invoice.SendEmail;
                    existing.DefaultInterest          = invoice.DefaultInterest;
                    existing.CapitalGainsTax          = invoice.CapitalGainsTax;
                    existing.AccountingCost           = invoice.AccountingCost;
                    existing.Ocr                      = invoice.Ocr;
                    existing.Source                   = invoice.Source;
                    existing.PaydayId                 = invoice.PaydayId;

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                }, logger, "updating sales invoice"))
            .WithName("UpdateSalesInvoice");

        // ── Delete ────────────────────────────────────────────────────────────
        securedApi.MapDelete("/sales-invoices/{id:guid}", async (
                WorkitDbContext db, HttpContext httpContext,
                Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    var invoice = await db.Invoices
                        .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
                    if (invoice is null) return Results.NotFound();

                    db.Invoices.Remove(invoice);
                    await db.SaveChangesAsync(ct);
                    return Results.NoContent();
                }, logger, "deleting sales invoice"))
            .WithName("DeleteSalesInvoice");

        // ── Lines ─────────────────────────────────────────────────────────────
        securedApi.MapPost("/sales-invoices/{id:guid}/lines", async (
                WorkitDbContext db, HttpContext httpContext,
                Guid id, InvoiceLine line, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;
                    if (!await db.Invoices.AnyAsync(x => x.Id == id && x.CompanyId == companyId, ct))
                        return Results.NotFound();

                    line.InvoiceId = id;
                    line.CompanyId = companyId;
                    db.InvoiceLines.Add(line);
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/sales-invoices/{id}/lines/{line.Id}", line);
                }, logger, "adding invoice line"))
            .WithName("AddInvoiceLine");

        securedApi.MapPut("/sales-invoices/{id:guid}/lines/{lineId:guid}", async (
                WorkitDbContext db, HttpContext httpContext,
                Guid id, Guid lineId, InvoiceLine line, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    if (lineId != line.Id) return Results.BadRequest("Line id mismatch.");
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    var existing = await db.InvoiceLines
                        .FirstOrDefaultAsync(x => x.Id == lineId && x.InvoiceId == id && x.CompanyId == companyId, ct);
                    if (existing is null) return Results.NotFound();

                    existing.Position                     = line.Position;
                    existing.Description                  = line.Description;
                    existing.Comment                      = line.Comment;
                    existing.Quantity                     = line.Quantity;
                    existing.UnitPriceExcludingVat        = line.UnitPriceExcludingVat;
                    existing.UnitPriceIncludingVat        = line.UnitPriceIncludingVat;
                    existing.ForeignUnitPriceExcludingVat = line.ForeignUnitPriceExcludingVat;
                    existing.ForeignUnitPriceIncludingVat = line.ForeignUnitPriceIncludingVat;
                    existing.VatPercentage                = line.VatPercentage;
                    existing.DiscountPercentage           = line.DiscountPercentage;
                    existing.Sku                          = line.Sku;
                    existing.ProductId                    = line.ProductId;
                    existing.PaydayId                     = line.PaydayId;

                    await db.SaveChangesAsync(ct);
                    return Results.Ok(existing);
                }, logger, "updating invoice line"))
            .WithName("UpdateInvoiceLine");

        securedApi.MapDelete("/sales-invoices/{id:guid}/lines/{lineId:guid}", async (
                WorkitDbContext db, HttpContext httpContext,
                Guid id, Guid lineId, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    var line = await db.InvoiceLines
                        .FirstOrDefaultAsync(x => x.Id == lineId && x.InvoiceId == id && x.CompanyId == companyId, ct);
                    if (line is null) return Results.NotFound();

                    db.InvoiceLines.Remove(line);
                    await db.SaveChangesAsync(ct);
                    return Results.NoContent();
                }, logger, "deleting invoice line"))
            .WithName("DeleteInvoiceLine");

        // ── Payments ──────────────────────────────────────────────────────────
        securedApi.MapPost("/sales-invoices/{id:guid}/payments", async (
                WorkitDbContext db, HttpContext httpContext,
                Guid id, InvoicePayment payment, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;
                    if (!await db.Invoices.AnyAsync(x => x.Id == id && x.CompanyId == companyId, ct))
                        return Results.NotFound();

                    payment.InvoiceId = id;
                    payment.CompanyId = companyId;
                    db.InvoicePayments.Add(payment);
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/sales-invoices/{id}/payments/{payment.Id}", payment);
                }, logger, "adding invoice payment"))
            .WithName("AddInvoicePayment");

        securedApi.MapDelete("/sales-invoices/{id:guid}/payments/{paymentId:guid}", async (
                WorkitDbContext db, HttpContext httpContext,
                Guid id, Guid paymentId, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                    var companyId = httpContext.User.ToUserContext().CompanyId;

                    var payment = await db.InvoicePayments
                        .FirstOrDefaultAsync(x => x.Id == paymentId && x.InvoiceId == id && x.CompanyId == companyId, ct);
                    if (payment is null) return Results.NotFound();

                    db.InvoicePayments.Remove(payment);
                    await db.SaveChangesAsync(ct);
                    return Results.NoContent();
                }, logger, "deleting invoice payment"))
            .WithName("DeleteInvoicePayment");
    }
}
