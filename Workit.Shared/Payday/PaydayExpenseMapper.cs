using Workit.Shared.Models;

namespace Workit.Shared.Payday;

public static class PaydayExpenseMapper
{
    /// <summary>Default markup applied to imported expense lines (50%).</summary>
    public const decimal DefaultMarkupFactor = 1.5m;

    /// <summary>
    /// Maps a Payday expense into a Workit <see cref="Expense"/> (Source = Payday), deriving a
    /// sale price per line from cost × markup. CompanyId is set on the server for the API path;
    /// pass it here for direct DB seeding.
    /// </summary>
    public static Expense ToWorkitExpense(this PaydayExpense p, Guid? jobId, Guid companyId = default)
    {
        var expense = new Expense
        {
            Id                  = Guid.NewGuid(),
            CompanyId           = companyId,
            CreditorPaydayId    = p.Creditor?.Id,
            CreditorSsn         = p.Creditor?.Ssn,
            CreditorName        = p.Creditor?.Name,
            PaymentTypePaydayId = p.PaymentType?.Id,
            PaymentTypeName     = p.PaymentType?.Name,
            Status              = string.IsNullOrWhiteSpace(p.Status) ? "UNPAID" : p.Status,
            Date                = p.Date,
            DueDate             = p.DueDate,
            FinalDueDate        = p.FinalDueDate,
            Reference           = p.Reference,
            Deductible          = p.Deductible,
            Comments            = p.Comments,
            Voucher             = p.Voucher,
            AmountExcludingVat  = p.AmountExcludingVat,
            AmountIncludingVat  = p.AmountIncludingVat,
            AmountVat           = p.AmountVat,
            JobId               = jobId,
            Source              = DataSource.Payday,
            PaydayId            = p.Id,
            CreatedAt           = DateTimeOffset.UtcNow,
        };

        expense.Lines = (p.Lines ?? []).Select(l =>
        {
            var cost = l.UnitPriceExcludingVat ?? 0m;
            return new ExpenseLine
            {
                Id                    = Guid.NewGuid(),
                ExpenseId             = expense.Id,
                CompanyId             = companyId,
                Description           = l.Description,
                Quantity              = l.Quantity,
                UnitPriceExcludingVat = cost,
                UnitPriceIncludingVat = l.UnitPriceIncludingVat,
                MarkupFactor          = DefaultMarkupFactor,
                SalePriceExcludingVat = Math.Round(cost * DefaultMarkupFactor, 0, MidpointRounding.AwayFromZero),
                VatPercentage         = l.VatPercentage,
                DiscountPercentage    = l.DiscountPercentage,
                AccountId             = l.AccountId,
                PaydayId              = l.Id,
            };
        }).ToList();

        return expense;
    }
}
