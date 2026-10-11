namespace Workit.Shared.Payday;

using Workit.Shared.Api;

/// <summary>
/// The outcome of checking Payday for invoices Workit still has records on.
/// <see cref="Unreachable"/> counts invoices Payday could not be asked about —
/// those are left exactly as they are, never released on a guess.
/// </summary>
public sealed record InvoiceReconcileResult(
    int Checked,
    int InvoicesReleased,
    int RecordsReleased,
    int NumbersFilled,
    int Unreachable);

/// <summary>
/// Asks the API to catch drafts deleted or cancelled inside Payday, so the hours,
/// materials and expense quantities they held can be billed again.
/// </summary>
public interface IInvoiceReconcileApi
{
    Task<ApiResult<InvoiceReconcileResult>> ReconcileAsync();
}

internal sealed class InvoiceReconcileProxy(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IInvoiceReconcileApi
{
    public Task<ApiResult<InvoiceReconcileResult>> ReconcileAsync() =>
        PostForJsonAsync<object, InvoiceReconcileResult>("api/payday/invoices/reconcile", new { }, "Invoices could not be checked against Payday right now.");
}
