using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Services;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal static class CompanyEndpoints
{
    internal static void MapCompanyEndpoints(this WebApplication app)
    {
        var securedApi = app.MapGroup("/api").RequireAuthorization().WithTags("Companies");
        var logger = app.Logger;
        var credentialProtection = app.Services.GetRequiredService<ICredentialProtectionService>();

        securedApi.MapGet("/company", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    // Available to every role: employees (mobile apps) need the company's
                    // name, driving rate and standard hours. Payday credentials are never
                    // returned — see WithoutSecrets.
                    var userContext = httpContext.User.ToUserContext();
                    var company = await db.Companies.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == userContext.CompanyId, ct);
                    if (company is null) return Results.NotFound();

                    return Results.Ok(company.WithoutSecrets());
                },
                logger,
                "loading company"))
            .WithName("GetCompany");

        securedApi.MapPost("/companies", async (WorkitDbContext db, HttpContext httpContext, Company request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                        return Results.Forbid();

                    var userContext = httpContext.User.ToUserContext();

                    var normalizedSsn = request.Ssn.Trim();
                    var existing = await db.Companies
                        .FirstOrDefaultAsync(x => x.Ssn == normalizedSsn, ct);

                    if (existing is not null)
                    {
                        // If the user already has access to this company, just return it
                        var alreadyLinked = await db.UserCompanies
                            .AnyAsync(x => x.UserId == userContext.UserId && x.CompanyId == existing.Id, ct);

                        if (alreadyLinked)
                            return Results.Conflict("You already have access to a company with that SSN.");

                        // Otherwise link the user to the existing company record
                        db.UserCompanies.Add(new UserCompany { UserId = userContext.UserId, CompanyId = existing.Id });
                        await db.SaveChangesAsync(ct);
                        return Results.Ok(existing.WithoutSecrets());
                    }

                    var company = new Company
                    {
                        Name               = request.Name.Trim(),
                        Ssn                = request.Ssn.Trim(),
                        Email              = request.Email.Trim(),
                        Address            = request.Address.Trim(),
                        Phone              = request.Phone.Trim(),
                        Owner              = request.Owner.Trim(),
                        ZipCode            = request.ZipCode.Trim(),
                        City               = request.City.Trim(),
                        VatNumber          = request.VatNumber.Trim(),
                        Source             = request.Source,
                        PaydayId           = request.PaydayId,
                        PaydayClientId     = string.IsNullOrWhiteSpace(request.PaydayClientId)     ? null : credentialProtection.Protect(request.PaydayClientId.Trim()),
                        PaydayClientSecret = string.IsNullOrWhiteSpace(request.PaydayClientSecret) ? null : credentialProtection.Protect(request.PaydayClientSecret.Trim())
                    };

                    var userCompany = new UserCompany
                    {
                        UserId = userContext.UserId,
                        CompanyId = company.Id
                    };

                    db.Companies.Add(company);
                    db.UserCompanies.Add(userCompany);
                    await db.SaveChangesAsync(ct);

                    return Results.Ok(company.WithoutSecrets());
                },
                logger,
                "creating a new company"))
            .WithName("CreateCompany");

        securedApi.MapPut("/company/driving-rate", async (WorkitDbContext db, HttpContext httpContext, UpdateDrivingRateRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwnerOrAdmin())
                        return Results.Forbid();

                    var userContext = httpContext.User.ToUserContext();
                    var company = await db.Companies.FirstOrDefaultAsync(x => x.Id == userContext.CompanyId, ct);
                    if (company is null) return Results.NotFound();

                    company.DrivingUnitPrice = request.UnitPrice;
                    await db.SaveChangesAsync(ct);
                    return Results.Ok(company.WithoutSecrets());
                },
                logger,
                "updating driving rate"))
            .WithName("UpdateDrivingRate");

        app.MapPut("/api/company/standard-hours", async (
            UpdateStandardHoursRequest req,
            WorkitDbContext db,
            HttpContext httpContext) =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
                return Results.Forbid();

            var userContext = httpContext.User.ToUserContext();
            var company = await db.Companies.FindAsync(userContext.CompanyId);
            if (company is null) return Results.NotFound();
            company.StandardHoursPerDay = req.StandardHoursPerDay;
            await db.SaveChangesAsync();
            return Results.Ok();
        }).RequireAuthorization().WithTags("Companies");

        app.MapPut("/api/company/payroll-items", async (
            UpdatePayrollItemNamesRequest req,
            WorkitDbContext db,
            HttpContext httpContext) =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
                return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.RegularItemName) || string.IsNullOrWhiteSpace(req.OvertimeItemName))
                return Results.BadRequest("Regular and overtime payroll item names are required.");

            var userContext = httpContext.User.ToUserContext();
            var company = await db.Companies.FindAsync(userContext.CompanyId);
            if (company is null) return Results.NotFound();
            company.PayrollRegularItemName  = req.RegularItemName.Trim();
            company.PayrollOvertimeItemName = req.OvertimeItemName.Trim();
            company.PayrollDrivingItemName  = req.DrivingItemName?.Trim() ?? string.Empty;
            await db.SaveChangesAsync();
            return Results.Ok();
        }).RequireAuthorization().WithTags("Companies");

        // ── Payday credential management ──

        app.MapPut("/api/company/payday-credentials", async (
            UpdatePaydayCredentialsRequest req,
            WorkitDbContext db,
            HttpContext httpContext) =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
                return Results.Forbid();

            var userContext = httpContext.User.ToUserContext();
            var company = await db.Companies.FindAsync(userContext.CompanyId);
            if (company is null) return Results.NotFound();

            company.PaydayClientId     = string.IsNullOrWhiteSpace(req.ClientId)     ? null : credentialProtection.Protect(req.ClientId.Trim());
            company.PaydayClientSecret = string.IsNullOrWhiteSpace(req.ClientSecret) ? null : credentialProtection.Protect(req.ClientSecret.Trim());
            await db.SaveChangesAsync();
            return Results.Ok();
        }).RequireAuthorization().WithTags("Companies");

        app.MapPost("/api/company/payday-test", async (
            UpdatePaydayCredentialsRequest req,
            HttpContext httpContext,
            IHttpClientFactory httpClientFactory) =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
                return Results.Forbid();

            if (string.IsNullOrWhiteSpace(req.ClientId) || string.IsNullOrWhiteSpace(req.ClientSecret))
                return Results.BadRequest("ClientId and ClientSecret are required.");

            try
            {
                var client = httpClientFactory.CreateClient("PaydayApi");
                var response = await client.PostAsJsonAsync("auth/token", new
                {
                    clientId = req.ClientId.Trim(),
                    clientSecret = req.ClientSecret.Trim()
                });

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    return Results.Json(new { success = false, message = $"Authentication failed ({(int)response.StatusCode}): {errorBody}" });
                }

                // Now fetch company info to display
                var tokenJson = await response.Content.ReadFromJsonAsync<JsonElement>();
                var accessToken = tokenJson.GetProperty("accessToken").GetString();

                using var companyRequest = new HttpRequestMessage(HttpMethod.Get, "companies/me");
                companyRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                var companyResponse = await client.SendAsync(companyRequest);

                if (companyResponse.IsSuccessStatusCode)
                {
                    var companyData = await companyResponse.Content.ReadFromJsonAsync<JsonElement>();
                    var companyName = companyData.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : "Unknown";
                    var companySsn  = companyData.TryGetProperty("ssn",  out var ssnProp)  ? ssnProp.GetString()  : "";
                    var companyEmail   = companyData.TryGetProperty("email",   out var emailProp)   ? emailProp.GetString()   : "";
                    var companyPhone   = companyData.TryGetProperty("phone",   out var phoneProp)   ? phoneProp.GetString()   : "";
                    var companyAddress = companyData.TryGetProperty("address", out var addrProp)    ? addrProp.GetString()    : "";
                    var companyZip     = companyData.TryGetProperty("zip",     out var zipProp)     ? zipProp.GetString()     : "";
                    var companyCity    = companyData.TryGetProperty("city",    out var cityProp)    ? cityProp.GetString()    : "";
                    var companyVatNumber = companyData.TryGetProperty("vatNumber", out var vatProp) ? vatProp.GetString()    : "";
                    Guid? companyId = companyData.TryGetProperty("id", out var idProp)
                        && idProp.ValueKind == JsonValueKind.String
                        && Guid.TryParse(idProp.GetString(), out var parsedId)
                            ? parsedId
                            : null;
                    return Results.Json(new
                    {
                        success     = true,
                        message     = $"Connected to Payday company: {companyName} ({companySsn})",
                        companyId,
                        companyName,
                        companySsn,
                        companyEmail,
                        companyPhone,
                        companyAddress,
                        companyZip,
                        companyCity,
                        companyVatNumber
                    });
                }

                return Results.Json(new { success = true, message = "Authentication successful, but could not fetch company details.", companyName = (string?)null, companySsn = (string?)null, companyEmail = (string?)null, companyPhone = (string?)null, companyAddress = (string?)null });
            }
            catch (Exception ex)
            {
                return Results.Json(new { success = false, message = $"Connection error: {ex.Message}" });
            }
        }).RequireAuthorization().WithTags("Companies");
    }
}

internal record UpdateDrivingRateRequest(decimal UnitPrice);
internal record UpdateStandardHoursRequest(decimal StandardHoursPerDay);
internal record UpdatePaydayCredentialsRequest(string? ClientId, string? ClientSecret);
