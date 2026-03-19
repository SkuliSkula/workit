using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;
using Workit.Api.Data;
using Workit.Shared.Models;

namespace Workit.Api.Services;

/// <summary>
/// Connects to an IMAP mailbox, finds emails with PDF attachments,
/// parses them as vendor invoices and persists them to the database.
/// Also creates or updates Material catalog entries.
/// </summary>
public sealed class EmailScanService(
    WorkitDbContext db,
    InvoiceParserService parser,
    ILogger<EmailScanService> logger)
{
    // ── Public scan entry point ─────────────────────────────────────────────

    public async Task<ScanResult> ScanAsync(EmailSettings settings, Guid companyId)
    {
        var result = new ScanResult();
        try
        {
            using var client = new ImapClient();
            await client.ConnectAsync(settings.ImapHost, settings.ImapPort, settings.UseSsl);
            await client.AuthenticateAsync(settings.Username, settings.Password);

            var folder = await OpenFolderAsync(client, settings.InvoiceFolder);
            var uids   = await folder.SearchAsync(SearchQuery.NotSeen);

            foreach (var uid in uids)
            {
                try
                {
                    var message = await folder.GetMessageAsync(uid);
                    await ProcessMessageAsync(message, companyId, result);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to process message UID {Uid}", uid);
                    result.Errors++;
                }
            }

            await client.DisconnectAsync(quit: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "IMAP scan failed for company {CompanyId}", companyId);
            result.FatalError = ex.Message;
        }

        settings.LastScannedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return result;
    }

    // ── IMAP test connection ────────────────────────────────────────────────

    public static async Task TestConnectionAsync(EmailSettings settings)
    {
        using var client = new ImapClient();
        await client.ConnectAsync(settings.ImapHost, settings.ImapPort, settings.UseSsl);
        await client.AuthenticateAsync(settings.Username, settings.Password);
        await client.DisconnectAsync(quit: true);
    }

    // ── Message processing ──────────────────────────────────────────────────

    private async Task ProcessMessageAsync(MimeMessage message, Guid companyId, ScanResult result)
    {
        string messageId = message.MessageId ?? Guid.NewGuid().ToString();

        // Dedup check
        bool exists = db.VendorInvoices.Any(v => v.CompanyId == companyId && v.SourceEmailMessageId == messageId);
        if (exists)
        {
            result.Skipped++;
            return;
        }

        // Find PDF attachments
        var pdfs = message.Attachments
            .OfType<MimePart>()
            .Where(p => p.ContentType.MimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
                     || p.FileName?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        if (pdfs.Count == 0)
        {
            result.Skipped++;
            return;
        }

        foreach (var pdf in pdfs)
        {
            using var ms = new MemoryStream();
            if (pdf.Content is null) { result.Skipped++; continue; }
            await pdf.Content.DecodeToAsync(ms);
            byte[] bytes = ms.ToArray();

            var parsed = await parser.ParseAsync(bytes, message.Subject ?? "(no subject)");
            if (parsed is null)
            {
                result.Errors++;
                continue;
            }

            var invoice = ToInvoice(parsed, companyId, message);
            db.VendorInvoices.Add(invoice);

            await ImportLineItemsAsync(invoice, companyId);

            result.Imported++;
        }
    }

    // ── Material catalog import ─────────────────────────────────────────────

    private async Task ImportLineItemsAsync(VendorInvoice invoice, Guid companyId)
    {
        foreach (var item in invoice.LineItems)
        {
            if (string.IsNullOrWhiteSpace(item.ProductCode)) continue;

            var material = db.Materials
                .FirstOrDefault(m => m.CompanyId == companyId && m.ProductCode == item.ProductCode);

            if (material is null)
            {
                // Create new material
                material = new Material
                {
                    CompanyId    = companyId,
                    Name         = item.Description,
                    ProductCode  = item.ProductCode,
                    Unit         = string.IsNullOrWhiteSpace(item.Unit) ? "stk." : item.Unit,
                    PurchasePrice = item.PurchasePrice,
                    MarkupFactor  = 1.5m,
                    UnitPrice     = Math.Round(item.PurchasePrice * 1.5m, 2),
                    VatRate       = 24.0m,
                    Quantity      = 0,
                    IsActive      = true
                };
                db.Materials.Add(material);
            }
            else if (material.PurchasePrice != item.PurchasePrice)
            {
                // Price changed — update purchase price and recompute sale price
                material.PurchasePrice = item.PurchasePrice;
                material.UnitPrice     = Math.Round(item.PurchasePrice * material.MarkupFactor, 2);
            }

            item.IsImported         = true;
            item.ImportedMaterialId = material.Id;
        }

        await Task.CompletedTask; // SaveChanges is called by the caller
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<IMailFolder> OpenFolderAsync(ImapClient client, string folderName)
    {
        if (folderName.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
            return client.Inbox;

        var ns = client.PersonalNamespaces.FirstOrDefault();
        if (ns is not null)
        {
            var folder = await client.GetFolderAsync(folderName);
            return folder;
        }

        return client.Inbox;
    }

    private static VendorInvoice ToInvoice(ParsedInvoice parsed, Guid companyId, MimeMessage message)
    {
        DateOnly date = DateOnly.TryParse(parsed.InvoiceDate, out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow);

        var invoice = new VendorInvoice
        {
            CompanyId            = companyId,
            InvoiceNumber        = parsed.InvoiceNumber,
            VendorName           = parsed.VendorName,
            VendorEmail          = parsed.VendorEmail,
            InvoiceDate          = date,
            SubtotalExVat        = parsed.SubtotalExVat,
            VatAmount            = parsed.VatAmount,
            TotalInclVat         = parsed.TotalInclVat,
            SourceEmailSubject   = message.Subject ?? string.Empty,
            SourceEmailMessageId = message.MessageId ?? Guid.NewGuid().ToString(),
            ReceivedAt           = message.Date == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : message.Date
        };

        foreach (var li in parsed.LineItems)
        {
            invoice.LineItems.Add(new VendorInvoiceLineItem
            {
                CompanyId      = companyId,
                InvoiceId      = invoice.Id,
                ProductCode    = li.ProductCode,
                Description    = li.Description,
                Quantity       = li.Quantity,
                Unit           = li.Unit,
                ListPrice      = li.ListPrice,
                DiscountPercent = li.DiscountPercent,
                PurchasePrice  = li.PurchasePrice
            });
        }

        return invoice;
    }
}

public sealed class ScanResult
{
    public int    Imported   { get; set; }
    public int    Skipped    { get; set; }
    public int    Errors     { get; set; }
    public string? FatalError { get; set; }
    public bool   Success    => FatalError is null;
}
