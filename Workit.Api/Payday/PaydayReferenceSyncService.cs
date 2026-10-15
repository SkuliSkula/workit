using Workit.Api.Data;
using Workit.Shared.Payday;

namespace Workit.Api.Payday;

/// <summary>
/// Keeps Workit's snapshots of the company's small Payday reference data
/// current: the company profile, Payday users, expense accounts, payment
/// types, sales ledger accounts and pension funds (see <see cref="PaydaySnapshotStore"/>).
/// Each is refreshed on its own; one Payday failure keeps that one's last copy
/// and does not stop the rest. Callers set the company's Payday credentials first.
/// </summary>
public sealed class PaydayReferenceSyncService(
    WorkitDbContext db,
    IPaydayCompaniesApi companies,
    IPaydayUsersApi users,
    IPaydayExpensesApi expenses,
    IPaydayProductsApi products,
    IPaydayPensionApi pension,
    ILogger<PaydayReferenceSyncService> logger)
{
    /// <returns>How many of the lists were refreshed, and how many Payday refused.</returns>
    public async Task<(int Refreshed, int Failed)> SyncAsync(Guid companyId, CancellationToken ct)
    {
        int refreshed = 0, failed = 0;
        async Task Keep<T>(string kind, Task<Workit.Shared.Api.ApiResult<T>> fetch)
        {
            var result = await fetch;
            if (result.IsSuccess && result.Value is not null)
            {
                await PaydaySnapshotStore.SaveAsync(db, companyId, kind, result.Value, ct);
                refreshed++;
            }
            else
            {
                logger.LogWarning("Payday {Kind} for {Company} not refreshed: {Error}", kind, companyId, result.ErrorMessage);
                failed++;
            }
        }

        await Keep(PaydaySnapshotStore.Company, companies.GetMeAsync());
        await Keep(PaydaySnapshotStore.Users, users.GetAllAsync());
        await Keep(PaydaySnapshotStore.ExpenseAccounts, expenses.GetAccountsAsync());
        await Keep(PaydaySnapshotStore.PaymentTypes, expenses.GetPaymentTypesAsync());
        await Keep(PaydaySnapshotStore.LedgerAccounts, products.GetSalesLedgerAccountsAsync());
        foreach (var type in Enum.GetValues<PaydayPensionType>())
            await Keep(PaydaySnapshotStore.PensionFunds(type), pension.GetByTypeAsync(type));
        return (refreshed, failed);
    }
}
