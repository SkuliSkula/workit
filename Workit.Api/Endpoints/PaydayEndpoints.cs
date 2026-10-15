using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Payday;
using Workit.Api.Services;
using Workit.Shared.Api;
using Workit.Shared.Payday;

namespace Workit.Api.Endpoints;

/// <summary>
/// Proxies Payday API calls for the caller's company. The company's Payday
/// credentials are decrypted here, in-process, and never returned to any client.
/// Every route is Owner/Admin only (enforced by <see cref="PaydayCredentialsFilter"/>).
/// </summary>
internal static class PaydayEndpoints
{
    internal static void MapPaydayEndpoints(this WebApplication app)
    {
        var payday = app.MapGroup("/api/payday")
            .RequireAuthorization()
            .AddEndpointFilter<PaydayCredentialsFilter>()
            .WithTags("Payday");

        // ── Users / company ───────────────────────────────────────────────────
        payday.MapGet("/users/me",       async (IPaydayUsersApi api) => ToResult(await api.GetMeAsync()));
        payday.MapGet("/users",          async (IPaydayUsersApi api) => ToResult(await api.GetAllAsync()));
        payday.MapGet("/users/{id}",     async (IPaydayUsersApi api, string id) => ToResult(await api.GetByIdAsync(id)));
        payday.MapGet("/companies/me",   async (IPaydayCompaniesApi api) => ToResult(await api.GetMeAsync()));

        // ── Customers ─────────────────────────────────────────────────────────
        // Workit's customers are the stored copy (PaydayCustomerSyncService keeps them in
        // step both ways); the list answers from them, in Payday's shape. Opening one,
        // or a write made straight to Payday, stores Payday's answer on the way through.
        payday.MapGet("/customers", async (WorkitDbContext db, HttpContext http, int page = 1, int perPage = 100, CancellationToken ct = default) =>
            Results.Ok(await PaydayCustomerStore.ListAsync(db, http.User.ToUserContext().CompanyId, page, perPage, ct)));
        payday.MapGet("/customers/{id}", async (IPaydayCustomersApi api, PaydayCustomerSyncService sync, WorkitDbContext db, HttpContext http, string id, CancellationToken ct) =>
        {
            var result = await api.GetByIdAsync(id);
            var companyId = http.User.ToUserContext().CompanyId;
            if (result.IsSuccess && result.Value is not null)
            {
                await sync.SaveAsync(companyId, result.Value, ct);
                return ToResult(result);
            }
            // Payday unreachable: the stored copy answers.
            var stored = Guid.TryParse(id, out var customerId) && !result.IsNotFound
                ? await PaydayCustomerStore.GetAsync(db, companyId, customerId, ct)
                : null;
            return stored is not null ? Results.Ok(stored) : ToResult(result);
        });
        payday.MapPost("/customers", async (IPaydayCustomersApi api, PaydayCustomerSyncService sync, HttpContext http, CreateCustomerRequest body, CancellationToken ct) =>
        {
            var result = await api.CreateAsync(body);
            if (result.IsSuccess && result.Value is not null)
                await sync.SaveAsync(http.User.ToUserContext().CompanyId, result.Value, ct);
            return ToResult(result);
        });
        payday.MapPut("/customers/{id}", async (IPaydayCustomersApi api, PaydayCustomerSyncService sync, HttpContext http, string id, UpdateCustomerRequest body, CancellationToken ct) =>
        {
            var result = await api.UpdateAsync(id, body);
            if (result.IsSuccess && result.Value is not null)
                await sync.SaveAsync(http.User.ToUserContext().CompanyId, result.Value, ct);
            return ToResult(result);
        });
        // Answered from Workit's stored invoices (see the Invoices section).
        payday.MapGet("/customers/{id}/invoices", async (WorkitDbContext db, HttpContext http, string id, int page = 1, int perPage = 25, CancellationToken ct = default) =>
            Guid.TryParse(id, out var customerId)
                ? Results.Ok(await PaydayInvoiceStore.ListAsync(db, http.User.ToUserContext().CompanyId,
                    new PaydayInvoiceStore.ListQuery(page, perPage, Include: "lines", CustomerId: customerId), ct))
                : Results.BadRequest("Not a Payday customer id."));
        payday.MapGet("/customers/{id}/statement", async (IPaydayCustomersApi api, string id, string dateFrom, string dateTo, int page = 1, int perPage = 100) => ToResult(await api.GetAccountStatementAsync(id, dateFrom, dateTo, page, perPage)));

        // ── Employees ─────────────────────────────────────────────────────────
        payday.MapGet("/employees",         async (IPaydayEmployeesApi api) => ToResult(await api.GetAllAsync()));
        payday.MapGet("/employees/{id}",    async (IPaydayEmployeesApi api, string id) => ToResult(await api.GetByIdAsync(id)));
        payday.MapPost("/employees",        async (IPaydayEmployeesApi api, CreateEmployeeRequest body) => ToResult(await api.CreateAsync(body)));
        payday.MapPut("/employees/{id}",    async (IPaydayEmployeesApi api, string id, UpdateEmployeeRequest body) => ToResult(await api.UpdateAsync(id, body)));
        payday.MapDelete("/employees/{id}", async (IPaydayEmployeesApi api, string id) => ToResult(await api.DeleteAsync(id)));

        // ── Pension / payroll ─────────────────────────────────────────────────
        payday.MapGet("/pension/funds/{type:int}", async (IPaydayPensionApi api, int type) => ToResult(await api.GetByTypeAsync((PaydayPensionType)type)));
        payday.MapPost("/payroll/timesheet",       async (IPaydayPayrollApi api, List<TimesheetEntry> body) => ToResult(await api.UploadTimesheetAsync(body)));

        // ── Invoices ──────────────────────────────────────────────────────────
        // Workit keeps its own copy of every Payday invoice (PaydayInvoiceSyncService)
        // and the list answers from it, in Payday's shape and with Payday's filters.
        // Everything Payday hands back — a created, edited or opened invoice — is
        // stored on the way through, and /invoices/sync refreshes the whole copy.
        payday.MapGet("/invoices", async (
                WorkitDbContext db,
                HttpContext http,
                int     page          = 1,
                int     perPage       = 25,
                string? include       = "lines",
                string? dateFrom      = null,
                string? dateTo        = null,
                string? excludeStatus = null,
                Guid?   customerId    = null,
                string? query         = null,
                string  order         = "desc",
                string  orderBy       = "number",
                CancellationToken ct  = default) =>
            Results.Ok(await PaydayInvoiceStore.ListAsync(db, http.User.ToUserContext().CompanyId,
                new PaydayInvoiceStore.ListQuery(page, perPage, include, dateFrom, dateTo, excludeStatus, customerId, query, order, orderBy), ct)));

        payday.MapPost("/invoices/sync", async (PaydayInvoiceSyncService sync, HttpContext http, CancellationToken ct) =>
        {
            var outcome = await sync.SyncAsync(http.User.ToUserContext().CompanyId, ct);
            return outcome.Error is null
                ? Results.Ok(outcome.Result)
                : Results.Content(outcome.Error, "text/plain", statusCode: StatusCodes.Status502BadGateway);
        });
        payday.MapGet("/invoices/sync", async (WorkitDbContext db, HttpContext http, CancellationToken ct) =>
            Results.Ok(await PaydayInvoiceStore.StatusAsync(db, http.User.ToUserContext().CompanyId, ct)));

        // Opening an invoice asks Payday for the latest and stores it. If Payday is
        // unreachable the stored copy answers; if Payday says it is gone, so is the copy.
        payday.MapGet("/invoices/{id}", async (IPaydayInvoicesApi api, PaydayInvoiceSyncService sync, WorkitDbContext db, HttpContext http, string id, string? include = "lines,payments", CancellationToken ct = default) =>
        {
            var result = await api.GetByIdAsync(id, include);
            if (!Guid.TryParse(id, out var invoiceId)) return ToResult(result);
            var companyId = http.User.ToUserContext().CompanyId;
            if (result.IsSuccess && result.Value is not null)
            {
                await sync.SaveAsync(companyId, result.Value, ct);
                return ToResult(result);
            }
            if (result.IsNotFound)
            {
                await sync.RemoveAsync(companyId, invoiceId, ct);
                return ToResult(result);
            }
            var stored = await PaydayInvoiceStore.GetAsync(db, companyId, invoiceId, include, ct);
            return stored is not null ? Results.Ok(stored) : ToResult(result);
        });
        payday.MapPost("/invoices", async (IPaydayInvoicesApi api, PaydayInvoiceSyncService sync, HttpContext http, CreateInvoiceRequest body, CancellationToken ct) =>
        {
            var result = await api.CreateAsync(body);
            if (result.IsSuccess && result.Value is not null)
                await sync.SaveAsync(http.User.ToUserContext().CompanyId, result.Value, ct);
            return ToResult(result);
        });
        // Cancelling an invoice, or issuing a draft, changes what Workit's records
        // should say: a cancelled invoice bills nothing, so its hours, materials and
        // expense quantities come back; an issued one now has a number to show.
        payday.MapPut("/invoices/{id}", async (IPaydayInvoicesApi api, PaydayInvoiceSyncService sync, WorkitDbContext db, HttpContext http, string id, UpdateInvoiceRequest body, CancellationToken ct) =>
        {
            var result = await api.UpdateAsync(id, body);
            if (result.IsSuccess && Guid.TryParse(id, out var invoiceId))
            {
                var companyId = http.User.ToUserContext().CompanyId;
                if (result.Value is not null)
                    await sync.SaveAsync(companyId, result.Value, ct);
                if (InvoiceRelease.ReleasesRecords(body.Status) || InvoiceRelease.ReleasesRecords(result.Value?.Status))
                    await InvoiceRelease.ReleaseAsync(db, companyId, invoiceId, ct);
                else if (result.Value?.Number is int number)
                    await InvoiceRelease.BackfillNumberAsync(db, companyId, invoiceId, number, ct);
            }
            return ToResult(result);
        });

        // Deleting a draft from Workit hands back everything it held. Only after
        // Payday confirms the delete — a draft that is still there keeps its records.
        payday.MapDelete("/invoices/{id}", async (IPaydayInvoicesApi api, PaydayInvoiceSyncService sync, WorkitDbContext db, HttpContext http, string id, CancellationToken ct) =>
        {
            var result = await api.DeleteAsync(id);
            if (result.IsSuccess && Guid.TryParse(id, out var invoiceId))
            {
                var companyId = http.User.ToUserContext().CompanyId;
                await InvoiceRelease.ReleaseAsync(db, companyId, invoiceId, ct);
                await sync.RemoveAsync(companyId, invoiceId, ct);
            }
            return ToResult(result);
        });

        // Catches drafts deleted or cancelled inside Payday itself. Run when the
        // invoice pages open; it asks Payday about each invoice Workit still has
        // records on, and releases only on a definite "gone" or "cancelled".
        payday.MapPost("/invoices/reconcile", async (IPaydayInvoicesApi api, WorkitDbContext db, HttpContext http, CancellationToken ct) =>
            Results.Ok(await InvoiceRelease.ReconcileAsync(db, api, http.User.ToUserContext().CompanyId, ct)));
        payday.MapGet("/invoices/{id}/pdf",        async (IPaydayInvoicesApi api, string id) => ToFileResult(await api.GetPdfAsync(id), "application/pdf"));
        payday.MapGet("/invoices/{id}/attachment", async (IPaydayInvoicesApi api, string id) => ToFileResult(await api.GetAttachmentAsync(id), "application/octet-stream"));

        // ── Expenses ──────────────────────────────────────────────────────────
        // Like invoices: Workit keeps its own copy of every Payday expense
        // (PaydayExpenseSyncService), the list answers from it, and whatever Payday
        // hands back on a write or an open is stored on the way through.
        payday.MapGet("/expenses", async (
                WorkitDbContext db,
                HttpContext http,
                int     page     = 1,
                int     perPage  = 25,
                string? include  = "lines",
                string? dateFrom = null,
                string? dateTo   = null,
                string? status   = null,
                string  order    = "desc",
                string  orderBy  = "date",
                CancellationToken ct = default) =>
            Results.Ok(await PaydayExpenseStore.ListAsync(db, http.User.ToUserContext().CompanyId,
                new PaydayExpenseStore.ListQuery(page, perPage, include, dateFrom, dateTo, status, order, orderBy), ct)));

        payday.MapPost("/expenses/sync", async (PaydayExpenseSyncService sync, HttpContext http, CancellationToken ct) =>
        {
            var outcome = await sync.SyncAsync(http.User.ToUserContext().CompanyId, ct);
            return outcome.Error is null
                ? Results.Ok(outcome.Result)
                : Results.Content(outcome.Error, "text/plain", statusCode: StatusCodes.Status502BadGateway);
        });
        payday.MapGet("/expenses/sync", async (WorkitDbContext db, HttpContext http, CancellationToken ct) =>
            Results.Ok(await PaydayExpenseStore.StatusAsync(db, http.User.ToUserContext().CompanyId, ct)));

        payday.MapGet("/expenses/paymenttypes",    async (IPaydayExpensesApi api) => ToResult(await api.GetPaymentTypesAsync()));
        payday.MapGet("/expenses/accounts",        async (IPaydayExpensesApi api) => ToResult(await api.GetAccountsAsync()));

        // Opening an expense asks Payday for the latest and stores it; the stored
        // copy answers when Payday can't, and goes when Payday says it is gone.
        payday.MapGet("/expenses/{id}", async (IPaydayExpensesApi api, PaydayExpenseSyncService sync, WorkitDbContext db, HttpContext http, string id, string? include = "lines", CancellationToken ct = default) =>
        {
            var result = await api.GetByIdAsync(id, include);
            if (!Guid.TryParse(id, out var expenseId)) return ToResult(result);
            var companyId = http.User.ToUserContext().CompanyId;
            if (result.IsSuccess && result.Value is not null)
            {
                await sync.SaveAsync(companyId, result.Value, ct);
                return ToResult(result);
            }
            if (result.IsNotFound)
            {
                await sync.RemoveAsync(companyId, expenseId, ct);
                return ToResult(result);
            }
            var stored = await PaydayExpenseStore.GetAsync(db, companyId, expenseId, include, ct);
            return stored is not null ? Results.Ok(stored) : ToResult(result);
        });
        payday.MapPost("/expenses", async (IPaydayExpensesApi api, PaydayExpenseSyncService sync, HttpContext http, CreateExpenseRequest body, CancellationToken ct) =>
        {
            var result = await api.CreateAsync(body);
            if (result.IsSuccess && result.Value is not null)
                await sync.SaveAsync(http.User.ToUserContext().CompanyId, result.Value, ct);
            return ToResult(result);
        });
        payday.MapPut("/expenses/{id}", async (IPaydayExpensesApi api, PaydayExpenseSyncService sync, HttpContext http, string id, UpdateExpenseRequest body, CancellationToken ct) =>
        {
            var result = await api.UpdateAsync(id, body);
            if (result.IsSuccess && result.Value is not null)
                await sync.SaveAsync(http.User.ToUserContext().CompanyId, result.Value, ct);
            return ToResult(result);
        });
        payday.MapDelete("/expenses/{id}", async (IPaydayExpensesApi api, PaydayExpenseSyncService sync, HttpContext http, string id, CancellationToken ct) =>
        {
            var result = await api.DeleteAsync(id);
            if (result.IsSuccess && Guid.TryParse(id, out var expenseId))
                await sync.RemoveAsync(http.User.ToUserContext().CompanyId, expenseId, ct);
            return ToResult(result);
        });
        payday.MapGet("/expenses/{id}/attachment", async (IPaydayExpensesApi api, string id) => ToFileResult(await api.GetAttachmentAsync(id), "application/octet-stream"));
    }

    // Payday failures come back as 502 with the message as plain text, so the
    // proxy clients surface the same error text the direct clients used to.
    private static IResult ToResult<T>(ApiResult<T> result) =>
        result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Content(result.ErrorMessage ?? "Payday request failed.", "text/plain", statusCode: StatusCodes.Status502BadGateway);

    private static IResult ToFileResult(ApiResult<byte[]> result, string contentType) =>
        result.IsSuccess && result.Value is not null
            ? Results.File(result.Value, contentType)
            : Results.Content(result.ErrorMessage ?? "Payday request failed.", "text/plain", statusCode: StatusCodes.Status502BadGateway);
}

/// <summary>
/// Runs before every /api/payday route: rejects non-owners, then loads and decrypts
/// the caller's company Payday credentials into the request-scoped token service.
/// </summary>
internal sealed class PaydayCredentialsFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!http.User.IsOwnerOrAdmin())
            return Results.Forbid();

        UserContext userContext;
        try
        {
            userContext = http.User.ToUserContext();
        }
        catch (InvalidOperationException)
        {
            return Results.Unauthorized();
        }

        if (userContext.CompanyId == Guid.Empty)
            return NotConnected();

        var services   = http.RequestServices;
        var db         = services.GetRequiredService<WorkitDbContext>();
        var protection = services.GetRequiredService<ICredentialProtectionService>();
        var tokens     = services.GetRequiredService<IPaydayTokenService>();

        var credentials = await db.Companies
            .AsNoTracking()
            .Where(c => c.Id == userContext.CompanyId)
            .Select(c => new { c.PaydayClientId, c.PaydayClientSecret })
            .FirstOrDefaultAsync(http.RequestAborted);

        var clientId     = protection.Unprotect(credentials?.PaydayClientId);
        var clientSecret = protection.Unprotect(credentials?.PaydayClientSecret);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return NotConnected();

        tokens.SetCredentials(clientId, clientSecret);
        return await next(context);
    }

    private static IResult NotConnected() =>
        Results.Content(
            "Payday is not connected for this company. Add your Payday credentials under Payday → Settings.",
            "text/plain",
            statusCode: StatusCodes.Status412PreconditionFailed);
}
