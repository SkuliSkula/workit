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
                    var userContext = httpContext.User.ToUserContext();
                    // AsNoTracking so decrypted values never accidentally get saved back
                    var company = await db.Companies.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == userContext.CompanyId, ct);
                    if (company is null) return Results.NotFound();

                    // Decrypt Payday credentials before sending to client (transmitted over HTTPS)
                    company.PaydayClientId     = credentialProtection.Unprotect(company.PaydayClientId);
                    company.PaydayClientSecret = credentialProtection.Unprotect(company.PaydayClientSecret);

                    return Results.Ok(company);
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
                        return Results.Ok(existing);
                    }

                    var company = new Company
                    {
                        Name               = request.Name.Trim(),
                        Ssn                = request.Ssn.Trim(),
                        Email              = request.Email.Trim(),
                        Address            = request.Address.Trim(),
                        Phone              = request.Phone.Trim(),
                        Owner              = request.Owner.Trim(),
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

                    return Results.Ok(company);
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
                    return Results.Ok(company);
                },
                logger,
                "updating driving rate"))
            .WithName("UpdateDrivingRate");

        app.MapPut("/api/company/standard-hours", async (
            UpdateStandardHoursRequest req,
            WorkitDbContext db,
            HttpContext httpContext) =>
        {
            var userContext = httpContext.User.ToUserContext();
            var company = await db.Companies.FindAsync(userContext.CompanyId);
            if (company is null) return Results.NotFound();
            company.StandardHoursPerDay = req.StandardHoursPerDay;
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
            IHttpClientFactory httpClientFactory) =>
        {
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
                    return Results.Json(new
                    {
                        success     = true,
                        message     = $"Connected to Payday company: {companyName} ({companySsn})",
                        companyName,
                        companySsn,
                        companyEmail,
                        companyPhone,
                        companyAddress
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
