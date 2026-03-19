using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Workit.Shared.Models;

namespace Workit.Api.Services;

/// <summary>
/// Extracts structured invoice data from a PDF using PdfPig (text extraction)
/// and the Claude API (structured parsing — handles any vendor format).
/// </summary>
public sealed class InvoiceParserService(AnthropicClient anthropic, ILogger<InvoiceParserService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // ── Public API ─────────────────────────────────────────────────────────

    public async Task<ParsedInvoice?> ParseAsync(byte[] pdfBytes, string emailSubject)
    {
        string rawText;
        try
        {
            rawText = ExtractPdfText(pdfBytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PDF text extraction failed for email '{Subject}'", emailSubject);
            return null;
        }

        if (string.IsNullOrWhiteSpace(rawText))
        {
            logger.LogWarning("Empty PDF text for email '{Subject}'", emailSubject);
            return null;
        }

        try
        {
            return await CallClaudeAsync(rawText, emailSubject);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Claude parsing failed for email '{Subject}'", emailSubject);
            return null;
        }
    }

    // ── PDF text extraction ─────────────────────────────────────────────────

    private static string ExtractPdfText(byte[] pdfBytes)
    {
        using var doc = PdfDocument.Open(pdfBytes);
        var sb = new StringBuilder();
        foreach (Page page in doc.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }

    // ── Claude structured parsing ───────────────────────────────────────────

    private async Task<ParsedInvoice?> CallClaudeAsync(string text, string emailSubject)
    {
        string prompt = $$"""
            You are an invoice data extraction assistant.
            Extract all invoice data from the vendor invoice text below.
            Return ONLY a single JSON object exactly matching this schema (no markdown, no code fences, no extra text):

            {
              "invoiceNumber": "",
              "vendorName": "",
              "vendorEmail": "",
              "invoiceDate": "YYYY-MM-DD",
              "subtotalExVat": 0.00,
              "vatAmount": 0.00,
              "totalInclVat": 0.00,
              "lineItems": [
                {
                  "productCode": "",
                  "description": "",
                  "quantity": 0,
                  "unit": "",
                  "listPrice": 0.00,
                  "discountPercent": 0,
                  "purchasePrice": 0.00
                }
              ]
            }

            Rules:
            - purchasePrice = listPrice x (1 - discountPercent / 100). If no discount, purchasePrice = listPrice.
            - invoiceDate must be in YYYY-MM-DD format.
            - All monetary values are plain numbers (no currency symbols or thousand separators).
            - If a field cannot be determined, use "" for strings and 0 for numbers.

            Invoice text:
            {{text}}
            """;

        var messages = new List<Message>
        {
            new() { Role = RoleType.User, Content = [new TextContent { Text = prompt }] }
        };

        var request = new MessageParameters
        {
            Model     = AnthropicModels.Claude45Haiku,
            MaxTokens = 2048,
            Messages  = messages
        };

        var response = await anthropic.Messages.GetClaudeMessageAsync(request);
        string json  = response.Content.OfType<TextContent>().FirstOrDefault()?.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogWarning("Empty Claude response for '{Subject}'", emailSubject);
            return null;
        }

        // Strip accidental markdown code fences if present
        if (json.StartsWith("```"))
        {
            int start = json.IndexOf('{');
            int end   = json.LastIndexOf('}');
            if (start >= 0 && end > start)
                json = json[start..(end + 1)];
        }

        return JsonSerializer.Deserialize<ParsedInvoice>(json, JsonOpts);
    }
}

// ── DTOs returned by the parser ────────────────────────────────────────────

public sealed class ParsedInvoice
{
    public string InvoiceNumber  { get; set; } = string.Empty;
    public string VendorName     { get; set; } = string.Empty;
    public string VendorEmail    { get; set; } = string.Empty;
    public string InvoiceDate    { get; set; } = string.Empty;
    public decimal SubtotalExVat { get; set; }
    public decimal VatAmount     { get; set; }
    public decimal TotalInclVat  { get; set; }
    public List<ParsedLineItem> LineItems { get; set; } = [];
}

public sealed class ParsedLineItem
{
    public string  ProductCode     { get; set; } = string.Empty;
    public string  Description     { get; set; } = string.Empty;
    public decimal Quantity        { get; set; }
    public string  Unit            { get; set; } = "stk.";
    public decimal ListPrice       { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal PurchasePrice   { get; set; }
}
