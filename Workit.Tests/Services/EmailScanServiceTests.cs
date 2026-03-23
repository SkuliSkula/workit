using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Api.Services;
using Workit.Shared.Models;

namespace Workit.Tests.Services;

/// <summary>
/// Tests for the static mapping methods and ImportLineItemsAsync logic
/// in EmailScanService. We use reflection to call the private static methods
/// since they contain significant business logic.
/// </summary>
public class EmailScanServiceTests
{
    // --- ToInvoiceFromFile (static helper) ---

    private static VendorInvoice CallToInvoiceFromFile(ParsedInvoice parsed, Guid companyId, string fileName, string dedupKey)
    {
        // Use reflection to call the private static method
        var method = typeof(EmailScanService).GetMethod(
            "ToInvoiceFromFile",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        return (VendorInvoice)method!.Invoke(null, [parsed, companyId, fileName, dedupKey])!;
    }

    [Fact]
    public void ToInvoiceFromFile_MapsBasicFields()
    {
        var companyId = Guid.NewGuid();
        var parsed = new ParsedInvoice
        {
            InvoiceNumber = "INV-001",
            VendorName = "Acme Corp",
            VendorEmail = "acme@example.com",
            InvoiceDate = "2026-03-15",
            SubtotalExVat = 10000m,
            VatAmount = 2400m,
            TotalInclVat = 12400m
        };

        var invoice = CallToInvoiceFromFile(parsed, companyId, "test.pdf", "file:test.pdf");

        invoice.CompanyId.Should().Be(companyId);
        invoice.InvoiceNumber.Should().Be("INV-001");
        invoice.VendorName.Should().Be("Acme Corp");
        invoice.VendorEmail.Should().Be("acme@example.com");
        invoice.InvoiceDate.Should().Be(new DateOnly(2026, 3, 15));
        invoice.SubtotalExVat.Should().Be(10000m);
        invoice.VatAmount.Should().Be(2400m);
        invoice.TotalInclVat.Should().Be(12400m);
        invoice.SourceEmailSubject.Should().Be("test.pdf");
        invoice.SourceEmailMessageId.Should().Be("file:test.pdf");
    }

    [Fact]
    public void ToInvoiceFromFile_InvalidDate_FallsBackToToday()
    {
        var parsed = new ParsedInvoice { InvoiceDate = "not-a-date" };

        var invoice = CallToInvoiceFromFile(parsed, Guid.NewGuid(), "test.pdf", "key");

        invoice.InvoiceDate.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    [Fact]
    public void ToInvoiceFromFile_EmptyDate_FallsBackToToday()
    {
        var parsed = new ParsedInvoice { InvoiceDate = "" };

        var invoice = CallToInvoiceFromFile(parsed, Guid.NewGuid(), "test.pdf", "key");

        invoice.InvoiceDate.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    [Fact]
    public void ToInvoiceFromFile_MapsLineItems()
    {
        var companyId = Guid.NewGuid();
        var parsed = new ParsedInvoice
        {
            InvoiceDate = "2026-01-01",
            LineItems =
            [
                new ParsedLineItem
                {
                    ProductCode = "ABC-001",
                    Description = "Widget",
                    Quantity = 5,
                    Unit = "stk.",
                    ListPrice = 1000m,
                    DiscountPercent = 10,
                    PurchasePrice = 900m
                },
                new ParsedLineItem
                {
                    ProductCode = "DEF-002",
                    Description = "Gadget",
                    Quantity = 2,
                    Unit = "m.",
                    ListPrice = 500m,
                    DiscountPercent = 0,
                    PurchasePrice = 500m
                }
            ]
        };

        var invoice = CallToInvoiceFromFile(parsed, companyId, "test.pdf", "key");

        invoice.LineItems.Should().HaveCount(2);

        var first = invoice.LineItems[0];
        first.CompanyId.Should().Be(companyId);
        first.ProductCode.Should().Be("ABC-001");
        first.Description.Should().Be("Widget");
        first.Quantity.Should().Be(5);
        first.ListPrice.Should().Be(1000m);
        first.DiscountPercent.Should().Be(10);
        first.PurchasePrice.Should().Be(900m);
    }

    // --- ImportLineItemsAsync (material catalog logic) ---

    private static WorkitDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<WorkitDbContext>()
            .UseInMemoryDatabase($"EmailScan-{Guid.NewGuid()}")
            .Options;
        return new WorkitDbContext(options);
    }

    private static async Task CallImportLineItemsAsync(EmailScanService service, VendorInvoice invoice, Guid companyId)
    {
        var method = typeof(EmailScanService).GetMethod(
            "ImportLineItemsAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(service, [invoice, companyId])!;
    }

    private static EmailScanService CreateService(WorkitDbContext db)
    {
        // parser and logger are not used in ImportLineItemsAsync
        return new EmailScanService(db, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<EmailScanService>.Instance);
    }

    [Fact]
    public async Task ImportLineItems_NewProduct_CreatesMaterial()
    {
        using var db = CreateInMemoryDb();
        var service = CreateService(db);
        var companyId = Guid.NewGuid();

        var invoice = new VendorInvoice
        {
            CompanyId = companyId,
            LineItems =
            [
                new VendorInvoiceLineItem
                {
                    CompanyId = companyId,
                    ProductCode = "NEW-001",
                    Description = "New Widget",
                    Quantity = 10,
                    Unit = "m.",
                    PurchasePrice = 500m
                }
            ]
        };

        await CallImportLineItemsAsync(service, invoice, companyId);
        await db.SaveChangesAsync();

        var material = await db.Materials.SingleAsync(m => m.ProductCode == "NEW-001");
        material.Name.Should().Be("New Widget");
        material.Quantity.Should().Be(10);
        material.PurchasePrice.Should().Be(500m);
        material.MarkupFactor.Should().Be(1.5m);
        material.UnitPrice.Should().Be(750m); // 500 * 1.5
        material.VatRate.Should().Be(24.0m);
        material.IsActive.Should().BeTrue();
        material.Unit.Should().Be("m.");

        invoice.LineItems[0].IsImported.Should().BeTrue();
        invoice.LineItems[0].ImportedMaterialId.Should().Be(material.Id);
    }

    [Fact]
    public async Task ImportLineItems_EmptyUnit_DefaultsToStk()
    {
        using var db = CreateInMemoryDb();
        var service = CreateService(db);
        var companyId = Guid.NewGuid();

        var invoice = new VendorInvoice
        {
            CompanyId = companyId,
            LineItems =
            [
                new VendorInvoiceLineItem
                {
                    CompanyId = companyId,
                    ProductCode = "X-001",
                    Description = "Thing",
                    Quantity = 1,
                    Unit = "",
                    PurchasePrice = 100m
                }
            ]
        };

        await CallImportLineItemsAsync(service, invoice, companyId);
        await db.SaveChangesAsync();

        var material = await db.Materials.SingleAsync(m => m.ProductCode == "X-001");
        material.Unit.Should().Be("stk.");
    }

    [Fact]
    public async Task ImportLineItems_ExistingProduct_AddsQuantity()
    {
        using var db = CreateInMemoryDb();
        var companyId = Guid.NewGuid();

        // Pre-existing material
        db.Materials.Add(new Material
        {
            CompanyId = companyId,
            ProductCode = "EXIST-001",
            Name = "Existing Widget",
            Quantity = 20,
            PurchasePrice = 500m,
            MarkupFactor = 1.5m,
            UnitPrice = 750m
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var invoice = new VendorInvoice
        {
            CompanyId = companyId,
            LineItems =
            [
                new VendorInvoiceLineItem
                {
                    CompanyId = companyId,
                    ProductCode = "EXIST-001",
                    Quantity = 15,
                    PurchasePrice = 500m // same price
                }
            ]
        };

        await CallImportLineItemsAsync(service, invoice, companyId);
        await db.SaveChangesAsync();

        var material = await db.Materials.SingleAsync(m => m.ProductCode == "EXIST-001");
        material.Quantity.Should().Be(35); // 20 + 15
        material.UnitPrice.Should().Be(750m); // unchanged
    }

    [Fact]
    public async Task ImportLineItems_ExistingProduct_PriceChange_RecalculatesUnitPrice()
    {
        using var db = CreateInMemoryDb();
        var companyId = Guid.NewGuid();

        db.Materials.Add(new Material
        {
            CompanyId = companyId,
            ProductCode = "PRICE-001",
            Name = "Widget",
            Quantity = 10,
            PurchasePrice = 500m,
            MarkupFactor = 2.0m,
            UnitPrice = 1000m // 500 * 2
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var invoice = new VendorInvoice
        {
            CompanyId = companyId,
            LineItems =
            [
                new VendorInvoiceLineItem
                {
                    CompanyId = companyId,
                    ProductCode = "PRICE-001",
                    Quantity = 5,
                    PurchasePrice = 600m // price increased
                }
            ]
        };

        await CallImportLineItemsAsync(service, invoice, companyId);
        await db.SaveChangesAsync();

        var material = await db.Materials.SingleAsync(m => m.ProductCode == "PRICE-001");
        material.Quantity.Should().Be(15); // 10 + 5
        material.PurchasePrice.Should().Be(600m); // updated
        material.UnitPrice.Should().Be(1200m); // 600 * 2.0
    }

    [Fact]
    public async Task ImportLineItems_EmptyProductCode_SkipsItem()
    {
        using var db = CreateInMemoryDb();
        var service = CreateService(db);
        var companyId = Guid.NewGuid();

        var invoice = new VendorInvoice
        {
            CompanyId = companyId,
            LineItems =
            [
                new VendorInvoiceLineItem
                {
                    CompanyId = companyId,
                    ProductCode = "",
                    Description = "No code",
                    Quantity = 1,
                    PurchasePrice = 100m
                }
            ]
        };

        await CallImportLineItemsAsync(service, invoice, companyId);
        await db.SaveChangesAsync();

        var materials = await db.Materials.ToListAsync();
        materials.Should().BeEmpty();
        invoice.LineItems[0].IsImported.Should().BeFalse();
    }

    [Fact]
    public async Task ImportLineItems_WhitespaceProductCode_SkipsItem()
    {
        using var db = CreateInMemoryDb();
        var service = CreateService(db);
        var companyId = Guid.NewGuid();

        var invoice = new VendorInvoice
        {
            CompanyId = companyId,
            LineItems =
            [
                new VendorInvoiceLineItem
                {
                    CompanyId = companyId,
                    ProductCode = "   ",
                    Quantity = 1,
                    PurchasePrice = 100m
                }
            ]
        };

        await CallImportLineItemsAsync(service, invoice, companyId);
        await db.SaveChangesAsync();

        var materials = await db.Materials.ToListAsync();
        materials.Should().BeEmpty();
    }
}
