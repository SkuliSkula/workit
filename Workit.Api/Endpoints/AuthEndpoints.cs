using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Api;
using Workit.Shared.Auth;
using Workit.Shared.Models;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal static class AuthEndpoints
{
    internal static void MapAuthEndpoints(this WebApplication app)
    {
        var authApi = app.MapGroup("/api/auth").WithTags("Auth");
        var logger = app.Logger;

        authApi.MapPost("/login", async (WorkitDbContext db, TokenFactory tokenFactory, LoginRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                    {
                        return Results.BadRequest("Email and password are required.");
                    }

                    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
                    var user = await db.AppUsers.FirstOrDefaultAsync(x => x.Email == normalizedEmail, ct);
                    if (user is null || !PasswordHasher.VerifyPassword(request.Password, user.PasswordHash))
                    {
                        return Results.Unauthorized();
                    }

                    var loginResponse = tokenFactory.CreateToken(user);
                    var refreshToken = tokenFactory.CreateRefreshToken(user.Id);
                    db.RefreshTokens.Add(refreshToken);
                    await db.SaveChangesAsync(ct);
                    loginResponse.RefreshToken = refreshToken.Token;
                    return Results.Ok(loginResponse);
                },
                logger,
                "authenticating a user"))
            .WithName("Login");

        authApi.MapPost("/refresh", async (WorkitDbContext db, TokenFactory tokenFactory, RefreshTokenRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (string.IsNullOrWhiteSpace(request.RefreshToken))
                    {
                        return Results.BadRequest("Refresh token is required.");
                    }

                    var stored = await db.RefreshTokens.FirstOrDefaultAsync(x => x.Token == request.RefreshToken, ct);
                    if (stored is null || stored.Revoked || stored.ExpiresUtc < DateTime.UtcNow)
                    {
                        return Results.Unauthorized();
                    }

                    var user = await db.AppUsers.FirstOrDefaultAsync(x => x.Id == stored.UserId, ct);
                    if (user is null)
                    {
                        return Results.Unauthorized();
                    }

                    // Revoke old refresh token and issue new pair
                    stored.Revoked = true;
                    var loginResponse = tokenFactory.CreateToken(user);
                    var newRefreshToken = tokenFactory.CreateRefreshToken(user.Id);
                    db.RefreshTokens.Add(newRefreshToken);
                    await db.SaveChangesAsync(ct);
                    loginResponse.RefreshToken = newRefreshToken.Token;
                    return Results.Ok(loginResponse);
                },
                logger,
                "refreshing a token"))
            .WithName("RefreshToken");

        authApi.MapGet("/companies", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();

                    // Admin sees ALL companies in the system
                    if (httpContext.User.IsAdmin())
                    {
                        var allCompanies = await db.Companies
                            .OrderBy(x => x.Name)
                            .ToListAsync(ct);
                        return Results.Ok(allCompanies);
                    }

                    var companyIds = await db.UserCompanies
                        .Where(x => x.UserId == userContext.UserId)
                        .Select(x => x.CompanyId)
                        .ToListAsync(ct);

                    var companies = await db.Companies
                        .Where(x => companyIds.Contains(x.Id))
                        .OrderBy(x => x.Name)
                        .ToListAsync(ct);

                    return Results.Ok(companies);
                },
                logger,
                "listing user companies"))
            .RequireAuthorization()
            .WithName("GetUserCompanies");

        authApi.MapPost("/switch-company", async (WorkitDbContext db, TokenFactory tokenFactory, HttpContext httpContext, SwitchCompanyRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    var userContext = httpContext.User.ToUserContext();

                    // Admin can switch to any company; owners need UserCompanies link
                    if (!httpContext.User.IsAdmin())
                    {
                        var hasAccess = await db.UserCompanies.AnyAsync(
                            x => x.UserId == userContext.UserId && x.CompanyId == request.CompanyId, ct);

                        if (!hasAccess)
                            return Results.Forbid();
                    }
                    else
                    {
                        // Verify the company actually exists
                        var companyExists = await db.Companies.AnyAsync(x => x.Id == request.CompanyId, ct);
                        if (!companyExists)
                            return Results.NotFound();
                    }

                    var user = await db.AppUsers.FirstOrDefaultAsync(x => x.Id == userContext.UserId, ct);
                    if (user is null)
                        return Results.Unauthorized();

                    // Issue new token with the selected company
                    var loginResponse = tokenFactory.CreateToken(user, request.CompanyId);
                    var refreshToken = tokenFactory.CreateRefreshToken(user.Id);
                    db.RefreshTokens.Add(refreshToken);
                    await db.SaveChangesAsync(ct);
                    loginResponse.RefreshToken = refreshToken.Token;
                    return Results.Ok(loginResponse);
                },
                logger,
                "switching company"))
            .RequireAuthorization()
            .WithName("SwitchCompany");

        // ── Admin: list all companies with owner details ──
        authApi.MapGet("/admin/companies", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsAdmin())
                        return Results.Forbid();

                    var companies = await db.Companies
                        .OrderBy(x => x.Name)
                        .ToListAsync(ct);

                    var ownerUsers = await db.AppUsers
                        .Where(x => x.Role == WorkitRoles.Owner)
                        .ToListAsync(ct);

                    // Build owner lookup: companyId → owner email (skip owners with no company yet)
                    var ownerLookup = ownerUsers
                        .Where(x => x.CompanyId.HasValue && x.CompanyId.Value != Guid.Empty)
                        .GroupBy(x => x.CompanyId!.Value)
                        .ToDictionary(g => g.Key, g => g.First().Email);

                    var result = companies.Select(c => new
                    {
                        c.Id,
                        c.Name,
                        c.Ssn,
                        c.Email,
                        c.Address,
                        c.Phone,
                        c.Owner,
                        OwnerEmail = ownerLookup.GetValueOrDefault(c.Id, "—"),
                        HasPayday = !string.IsNullOrWhiteSpace(c.PaydayClientId)
                    });

                    return Results.Ok(result);
                },
                logger,
                "listing all companies for admin"))
            .RequireAuthorization()
            .WithName("GetAdminCompanies");

        authApi.MapPost("/register-company", async (WorkitDbContext db, HttpContext httpContext, TokenFactory tokenFactory, RegisterCompanyRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsAdmin())
                    {
                        return Results.Forbid();
                    }

                    if (!IsValidCompany(request.Company))
                    {
                        return Results.BadRequest("Company name, SSN, email, address, phone, and owner are required.");
                    }

                    if (!IsValidCredentials(request.OwnerEmail, request.OwnerPassword))
                    {
                        return Results.BadRequest("Owner email and a password with at least 8 characters are required.");
                    }

                    var normalizedEmail = request.OwnerEmail.Trim().ToLowerInvariant();
                    if (await db.AppUsers.AnyAsync(x => x.Email == normalizedEmail, ct))
                    {
                        return Results.Conflict("That email address is already in use.");
                    }

                    var company = new Company
                    {
                        Name = request.Company.Name.Trim(),
                        Ssn = request.Company.Ssn.Trim(),
                        Email = request.Company.Email.Trim(),
                        Address = request.Company.Address.Trim(),
                        Phone = request.Company.Phone.Trim(),
                        Owner = request.Company.Owner.Trim()
                    };

                    var ownerUser = new AppUser
                    {
                        Email = normalizedEmail,
                        PasswordHash = PasswordHasher.HashPassword(request.OwnerPassword),
                        Role = WorkitRoles.Owner,
                        CompanyId = company.Id
                    };

                    db.Companies.Add(company);
                    db.AppUsers.Add(ownerUser);
                    db.UserCompanies.Add(new UserCompany { UserId = ownerUser.Id, CompanyId = company.Id });
                    await db.SaveChangesAsync(ct);

                    return Results.Ok(tokenFactory.CreateToken(ownerUser));
                },
                logger,
                "registering a company"))
            .RequireAuthorization()
            .WithName("RegisterCompany");

        // ── Admin: create an owner account with no company yet ──
        authApi.MapPost("/admin/create-owner", async (WorkitDbContext db, HttpContext httpContext, CreateOwnerRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsAdmin())
                        return Results.Forbid();

                    if (string.IsNullOrWhiteSpace(request.Name))
                        return Results.BadRequest("Owner name is required.");

                    if (!IsValidCredentials(request.Email, request.Password))
                        return Results.BadRequest("A valid email and a password of at least 8 characters are required.");

                    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
                    if (await db.AppUsers.AnyAsync(x => x.Email == normalizedEmail, ct))
                        return Results.Conflict("That email address is already in use.");

                    var owner = new AppUser
                    {
                        Name         = request.Name.Trim(),
                        Email        = normalizedEmail,
                        PasswordHash = PasswordHasher.HashPassword(request.Password),
                        Role         = WorkitRoles.Owner,
                        CompanyId    = null   // no company yet — owner will set it up on first login
                    };

                    db.AppUsers.Add(owner);
                    await db.SaveChangesAsync(ct);

                    return Results.Ok(new { owner.Id, owner.Email, owner.Name });
                },
                logger,
                "creating an owner account"))
            .RequireAuthorization()
            .WithName("CreateOwner");

        // ── Admin: list all owner accounts with company status ──
        authApi.MapGet("/admin/owners", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsAdmin())
                        return Results.Forbid();

                    var owners = await db.AppUsers
                        .Where(x => x.Role == WorkitRoles.Owner)
                        .OrderBy(x => x.Name)
                        .ToListAsync(ct);

                    var companyIds = owners
                        .Where(o => o.CompanyId.HasValue && o.CompanyId.Value != Guid.Empty)
                        .Select(o => o.CompanyId!.Value)
                        .Distinct()
                        .ToList();

                    var companyNames = await db.Companies
                        .Where(c => companyIds.Contains(c.Id))
                        .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

                    var result = owners.Select(o =>
                    {
                        var hasCompany = o.CompanyId.HasValue && o.CompanyId.Value != Guid.Empty;
                        return new AdminOwnerInfo
                        {
                            Id          = o.Id,
                            Name        = string.IsNullOrWhiteSpace(o.Name) ? o.Email : o.Name,
                            Email       = o.Email,
                            HasCompany  = hasCompany,
                            CompanyName = hasCompany && companyNames.TryGetValue(o.CompanyId!.Value, out var cn) ? cn : string.Empty
                        };
                    });

                    return Results.Ok(result.ToList());
                },
                logger,
                "listing all owners for admin"))
            .RequireAuthorization()
            .WithName("GetAdminOwners");

        // ── Owner: self-provision a company on first login ──
        authApi.MapPost("/owner/setup-company", async (WorkitDbContext db, HttpContext httpContext, TokenFactory tokenFactory, OwnerSetupCompanyRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsOwner())
                        return Results.Forbid();

                    var userContext = httpContext.User.ToUserContext();

                    // Guard: only allowed while the owner has no company yet
                    if (userContext.CompanyId != Guid.Empty)
                        return Results.Conflict("You already have a company. Use the Companies page to add another.");

                    if (!IsValidCompany(request.Company))
                        return Results.BadRequest("Company name, SSN, email, address, phone, and owner are required.");

                    var company = new Company
                    {
                        Name               = request.Company.Name.Trim(),
                        Ssn                = request.Company.Ssn.Trim(),
                        Email              = request.Company.Email.Trim(),
                        Address            = request.Company.Address.Trim(),
                        Phone              = request.Company.Phone.Trim(),
                        Owner              = request.Company.Owner.Trim(),
                        PaydayClientId     = request.PaydayClientId?.Trim(),
                        PaydayClientSecret = request.PaydayClientSecret?.Trim()
                    };

                    var user = await db.AppUsers.FirstOrDefaultAsync(x => x.Id == userContext.UserId, ct);
                    if (user is null)
                        return Results.Unauthorized();

                    user.CompanyId = company.Id;

                    db.Companies.Add(company);
                    db.UserCompanies.Add(new UserCompany { UserId = user.Id, CompanyId = company.Id });
                    await db.SaveChangesAsync(ct);

                    // Issue a fresh token with the new company_id embedded
                    var loginResponse = tokenFactory.CreateToken(user);
                    var refreshToken  = tokenFactory.CreateRefreshToken(user.Id);
                    db.RefreshTokens.Add(refreshToken);
                    await db.SaveChangesAsync(ct);
                    loginResponse.RefreshToken = refreshToken.Token;
                    return Results.Ok(loginResponse);
                },
                logger,
                "owner setting up company"))
            .RequireAuthorization()
            .WithName("OwnerSetupCompany");

        authApi.MapPost("/setup-company", async (WorkitDbContext db, HttpContext httpContext, SetupCompanyRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsAdmin())
                    {
                        return Results.Forbid();
                    }

                    if (await db.Companies.AnyAsync(ct))
                    {
                        return Results.Conflict("A company is already set up.");
                    }

                    if (!IsValidCompany(request.Company))
                    {
                        return Results.BadRequest("Company name, SSN, email, address, phone, and owner are required.");
                    }

                    var ownerEmail = request.OwnerEmail.Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(ownerEmail))
                    {
                        return Results.BadRequest("Owner email is required.");
                    }

                    if (await db.AppUsers.AnyAsync(x => x.Email == ownerEmail, ct))
                    {
                        return Results.Conflict("That email address is already in use.");
                    }

                    var password = GenerateOwnerPassword();

                    var company = new Company
                    {
                        Name    = request.Company.Name.Trim(),
                        Ssn     = request.Company.Ssn.Trim(),
                        Email   = request.Company.Email.Trim(),
                        Address = request.Company.Address.Trim(),
                        Phone   = request.Company.Phone.Trim(),
                        Owner   = request.Company.Owner.Trim()
                    };

                    db.Companies.Add(company);
                    var setupOwner = new AppUser
                    {
                        Email        = ownerEmail,
                        PasswordHash = PasswordHasher.HashPassword(password),
                        Role         = WorkitRoles.Owner,
                        CompanyId    = company.Id
                    };
                    db.AppUsers.Add(setupOwner);
                    db.UserCompanies.Add(new UserCompany { UserId = setupOwner.Id, CompanyId = company.Id });
                    await db.SaveChangesAsync(ct);

                    return Results.Ok(new SetupCompanyResponse
                    {
                        OwnerEmail        = ownerEmail,
                        GeneratedPassword = password
                    });
                },
                logger,
                "setting up company"))
            .RequireAuthorization()
            .WithName("SetupCompany");
    }
}

internal record SwitchCompanyRequest(Guid CompanyId);
