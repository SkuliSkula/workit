using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;

namespace Workit.Api.Payday;

/// <summary>
/// The one place a purchase line's sale price is set: its Ein.verð without VAT,
/// before the vendor's discount, no markup — each expense read the way its own
/// total says (<see cref="ExpenseUnitPrice"/>). Every path that adds or changes
/// purchase lines (the expense sync, the import, auto-link and manual routes) ends
/// by calling <see cref="RepriceAsync"/>, so none can carry a formula of its own:
/// two earlier pricing fixes (#70, #73) each missed one.
/// </summary>
internal static class ExpensePricing
{
    /// <summary>
    /// Re-prices every purchase line of the company that is still Workit's to price:
    /// not set by an owner, and not billed yet — a billed line's price is what the
    /// job page reports it was billed at, so it stays. Saves; returns how many changed.
    /// </summary>
    public static async Task<int> RepriceAsync(WorkitDbContext db, Guid companyId, CancellationToken ct)
    {
        var billed = (await db.ExpenseLineBillings.Where(b => b.CompanyId == companyId)
                .Select(b => b.ExpenseLineId).Distinct().ToListAsync(ct))
            .ToHashSet();

        var expenses = await db.Expenses
            .Include(e => e.Lines)
            .Where(e => e.CompanyId == companyId && e.Lines.Any(l => !l.SalePriceSetByOwner))
            .ToListAsync(ct);

        var changed = 0;
        foreach (var expense in expenses)
        {
            // Every line counts towards the total, priced by an owner or not.
            var reading = ExpenseUnitPrice.Read(expense.AmountExcludingVat, expense.Lines);
            foreach (var line in expense.Lines.Where(l => !l.SalePriceSetByOwner && !billed.Contains(l.Id)))
            {
                var sale = ExpenseUnitPrice.SalePrice(line, reading);
                if (sale == line.SalePriceExcludingVat && line.MarkupFactor == 1m) continue;
                line.SalePriceExcludingVat = sale;
                line.MarkupFactor = 1m;
                changed++;
            }
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }
}
