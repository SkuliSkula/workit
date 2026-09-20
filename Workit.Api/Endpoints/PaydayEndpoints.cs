using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
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
        payday.MapGet("/customers",         async (IPaydayCustomersApi api, int page = 1, int perPage = 100) => ToResult(await api.GetAllAsync(page, perPage)));
        payday.MapGet("/customers/{id}",    async (IPaydayCustomersApi api, string id) => ToResult(await api.GetByIdAsync(id)));
        payday.MapPost("/customers",        async (IPaydayCustomersApi api, CreateCustomerRequest body) => ToResult(await api.CreateAsync(body)));
        payday.MapPut("/customers/{id}",    async (IPaydayCustomersApi api, string id, UpdateCustomerRequest body) => ToResult(await api.UpdateAsync(id, body)));
        payday.MapGet("/customers/{id}/invoices",  async (IPaydayCustomersApi api, string id, int page = 1, int perPage = 25) => ToResult(await api.GetInvoicesAsync(id, page, perPage)));
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
        payday.MapGet("/invoices", async (
                IPaydayInvoicesApi api,
                int     page          = 1,
                int     perPage       = 25,
                string? include       = "lines",
                string? dateFrom      = null,
                string? dateTo        = null,
                string? excludeStatus = null,
                Guid?   customerId    = null,
                string? query         = null,
                string  order         = "desc",
                string  orderBy       = "number") =>
            ToResult(await api.GetAllAsync(page, perPage, include, dateFrom, dateTo, excludeStatus, customerId, query, order, orderBy)));
        payday.MapGet("/invoices/{id}",            async (IPaydayInvoicesApi api, string id, string? include = "lines,payments") => ToResult(await api.GetByIdAsync(id, include)));
        payday.MapPost("/invoices",                async (IPaydayInvoicesApi api, CreateInvoiceRequest body) => ToResult(await api.CreateAsync(body)));
        payday.MapPut("/invoices/{id}",            async (IPaydayInvoicesApi api, string id, UpdateInvoiceRequest body) => ToResult(await api.UpdateAsync(id, body)));
        payday.MapDelete("/invoices/{id}",         async (IPaydayInvoicesApi api, string id) => ToResult(await api.DeleteAsync(id)));
        payday.MapGet("/invoices/{id}/pdf",        async (IPaydayInvoicesApi api, string id) => ToFileResult(await api.GetPdfAsync(id), "application/pdf"));
        payday.MapGet("/invoices/{id}/attachment", async (IPaydayInvoicesApi api, string id) => ToFileResult(await api.GetAttachmentAsync(id), "application/octet-stream"));

        // ── Expenses ──────────────────────────────────────────────────────────
        payday.MapGet("/expenses", async (
                IPaydayExpensesApi api,
                int     page     = 1,
                int     perPage  = 25,
                string? include  = "lines",
                string? dateFrom = null,
                string? dateTo   = null,
                string? status   = null,
                string  order    = "desc",
                string  orderBy  = "date") =>
            ToResult(await api.GetAllAsync(page, perPage, include, dateFrom, dateTo, status, order, orderBy)));
        payday.MapGet("/expenses/paymenttypes",    async (IPaydayExpensesApi api) => ToResult(await api.GetPaymentTypesAsync()));
        payday.MapGet("/expenses/accounts",        async (IPaydayExpensesApi api) => ToResult(await api.GetAccountsAsync()));
        payday.MapGet("/expenses/{id}",            async (IPaydayExpensesApi api, string id, string? include = "lines") => ToResult(await api.GetByIdAsync(id, include)));
        payday.MapPost("/expenses",                async (IPaydayExpensesApi api, CreateExpenseRequest body) => ToResult(await api.CreateAsync(body)));
        payday.MapPut("/expenses/{id}",            async (IPaydayExpensesApi api, string id, UpdateExpenseRequest body) => ToResult(await api.UpdateAsync(id, body)));
        payday.MapDelete("/expenses/{id}",         async (IPaydayExpensesApi api, string id) => ToResult(await api.DeleteAsync(id)));
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
