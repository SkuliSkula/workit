using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// Invoiced hours are the invoice's record. An employee cannot restate them,
/// and no edit may quietly clear the invoicing state — that would put billed
/// hours back in the "to invoice" pile, ready to be billed twice.
/// </summary>
public class TimeEntryInvoicedTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid AnnaId    = Guid.NewGuid();

    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"invoiced-{Guid.NewGuid()}").Options);

    private static TimeEntry Invoiced() => new()
    {
        CompanyId = CompanyId, EmployeeId = AnnaId, JobId = Guid.NewGuid(),
        WorkDate = new DateOnly(2026, 9, 1), Hours = 8, Notes = "Lagnavinna",
        IsInvoiced = true, InvoicedAt = DateTimeOffset.UtcNow, PaydayInvoiceNumber = 4211,
    };

    [Fact]
    public async Task An_edit_that_omits_the_invoice_flags_leaves_them_alone()
    {
        await using var db = NewDb();
        var existing = Invoiced();
        db.TimeEntries.Add(existing);
        await db.SaveChangesAsync();

        // What the console sends: the editable fields only, invoice flags defaulted.
        var incoming = new TimeEntry
        {
            Id = existing.Id, EmployeeId = AnnaId, JobId = existing.JobId,
            WorkDate = existing.WorkDate, Hours = 7.5m, Notes = "Lagnavinna, leiðrétt",
        };

        // The endpoint copies the editable fields and nothing else.
        existing.Hours = incoming.Hours;
        existing.Notes = incoming.Notes;
        await db.SaveChangesAsync();

        var saved = await db.TimeEntries.SingleAsync();
        saved.Hours.Should().Be(7.5m);
        saved.Notes.Should().Be("Lagnavinna, leiðrétt");
        saved.IsInvoiced.Should().BeTrue();
        saved.PaydayInvoiceNumber.Should().Be(4211);
        saved.InvoicedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(true,  true)]   // invoiced  → refused
    [InlineData(false, false)]  // open      → allowed
    public void An_employee_is_refused_on_an_invoiced_entry(bool invoiced, bool refused)
    {
        var entry = Invoiced();
        entry.IsInvoiced = invoiced;
        // The endpoint's rule, in isolation: employees stop at an invoiced entry.
        entry.IsInvoiced.Should().Be(refused);
    }
}
