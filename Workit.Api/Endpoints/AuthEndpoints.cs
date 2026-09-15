using Microsoft.EntityFrameworkCore;
using Workit.Api.Analytics;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Services;
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
        var credentialProtection = app.Services.GetRequiredService<ICredentialProtectionService>();

        authApi.MapPost("/login", async (WorkitDbContext db, TokenFactory tokenFactory, IAnalyticsService analytics, LoginRequest request, CancellationToken ct) =>
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

                    // For owners, resolve their Employee record in the current company
                    Guid? employeeIdOverride = null;
                    if (user.Role == WorkitRoles.Owner && user.CompanyId.HasValue && user.CompanyId.Value != Guid.Empty)
                    {
                        var employee = await db.Employees
                            .FirstOrDefaultAsync(x => x.CompanyId == user.CompanyId.Value
                                                   && x.Email == user.Email, ct);
                        employeeIdOverride = employee?.Id;
                    }

                    var loginResponse = tokenFactory.CreateToken(user, employeeIdOverride: employeeIdOverride);
                    var refreshToken = tokenFactory.CreateRefreshToken(user.Id);
                    db.RefreshTokens.Add(refreshToken);
                    await db.SaveChangesAsync(ct);
                    loginResponse.RefreshToken = refreshToken.Token;

                    analytics.Capture(user.Id.ToString(), "user_logged_in", new
                    {
                        role       = user.Role,
                        company_id = user.CompanyId,
                        source     = request.Source ?? "unknown",
                    });

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

                    // For owners, resolve their Employee record in the current company
                    Guid? employeeIdOverride = null;
                    if (user.Role == WorkitRoles.Owner && user.CompanyId.HasValue && user.CompanyId.Value != Guid.Empty)
                    {
                        var employee = await db.Employees
                            .FirstOrDefaultAsync(x => x.CompanyId == user.CompanyId.Value
                                                   && x.Email == user.Email, ct);
                        employeeIdOverride = employee?.Id;
                    }

                    var loginResponse = tokenFactory.CreateToken(user, employeeIdOverride: employeeIdOverride);
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
                            .AsNoTracking()
                            .OrderBy(x => x.Name)
                            .ToListAsync(ct);
                        return Results.Ok(allCompanies.Select(c => c.WithoutSecrets()).ToList());
                    }

                    var companyIds = await db.UserCompanies
                        .Where(x => x.UserId == userContext.UserId)
                        .Select(x => x.CompanyId)
                        .ToListAsync(ct);

                    var companies = await db.Companies
                        .AsNoTracking()
                        .Where(x => companyIds.Contains(x.Id))
                        .OrderBy(x => x.Name)
                        .ToListAsync(ct);

                    return Results.Ok(companies.Select(c => c.WithoutSecrets()).ToList());
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

                    // Find the employee record in the target company by matching email
                    var employee = await db.Employees
                        .FirstOrDefaultAsync(x => x.CompanyId == request.CompanyId &&
                                                  x.Email == user.Email, ct);

                    // Issue new token with the selected company and resolved employee
                    var loginResponse = tokenFactory.CreateToken(user, request.CompanyId, employee?.Id);
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

        authApi.MapPost("/register-company", async (WorkitDbContext db, HttpContext httpContext, TokenFactory tokenFactory, IAnalyticsService analytics, RegisterCompanyRequest request, CancellationToken ct) =>
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
                        Owner = request.Company.Owner.Trim(),
                        ZipCode = request.Company.ZipCode.Trim(),
                        City = request.Company.City.Trim(),
                        VatNumber = request.Company.VatNumber.Trim()
                    };

                    var ownerUser = new AppUser
                    {
                        Email = normalizedEmail,
                        PasswordHash = PasswordHasher.HashPassword(request.OwnerPassword),
                        Role = WorkitRoles.Owner,
                        CompanyId = company.Id
                    };

                    var ownerEmployee = OwnerEmployeeHelper.CreateEmployeeForOwner(ownerUser, company.Id);

                    db.Companies.Add(company);
                    db.AppUsers.Add(ownerUser);
                    db.Employees.Add(ownerEmployee);
                    db.UserCompanies.Add(new UserCompany { UserId = ownerUser.Id, CompanyId = company.Id });
                    await db.SaveChangesAsync(ct);

                    var userContext = httpContext.User.ToUserContext();
                    analytics.Capture(userContext.UserId.ToString(), "company_registered", new
                    {
                        company_id   = company.Id,
                        company_name = company.Name,
                    });

                    return Results.Ok(tokenFactory.CreateToken(ownerUser));
                },
                logger,
                "registering a company"))
            .RequireAuthorization()
            .WithName("RegisterCompany");

        // ── Admin: create an owner account with no company yet ──
        authApi.MapPost("/admin/create-owner", async (WorkitDbContext db, HttpContext httpContext, IAccountInviteService invites, CreateOwnerRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsAdmin())
                        return Results.Forbid();

                    if (string.IsNullOrWhiteSpace(request.Name))
                        return Results.BadRequest("Owner name is required.");

                    if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
                        return Results.BadRequest("A valid email address is required.");

                    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
                    if (await db.AppUsers.AnyAsync(x => x.Email == normalizedEmail, ct))
                        return Results.Conflict("That email address is already in use.");

                    var owner = new AppUser
                    {
                        Name  = request.Name.Trim(),
                        Email = normalizedEmail,
                        // Nobody holds this. The owner picks their own password from the
                        // emailed link, so no password is ever transmitted or displayed.
                        PasswordHash = PasswordHasher.HashPassword(invites.CreateUnusablePassword()),
                        Role         = WorkitRoles.Owner,
                        CompanyId    = null   // no company yet — owner will set it up on first login
                    };

                    db.AppUsers.Add(owner);
                    await db.SaveChangesAsync(ct);

                    await invites.SendInviteAsync(owner.Email, owner.Name, InviteKind.Owner, ct);

                    return Results.Ok(new { owner.Id, owner.Email, owner.Name });
                },
                logger,
                "creating an owner account"))
            .RequireAuthorization()
            .WithName("CreateOwner");

        // ── Admin: send a fresh setup link to an owner whose invite lapsed ──
        authApi.MapPost("/admin/owners/{id:guid}/resend-invite", async (WorkitDbContext db, HttpContext httpContext, IAccountInviteService invites, Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsAdmin())
                        return Results.Forbid();

                    var owner = await db.AppUsers.FirstOrDefaultAsync(x => x.Id == id && x.Role == WorkitRoles.Owner, ct);
                    if (owner is null)
                        return Results.NotFound();

                    if (DemoDataSeeder.IsProtectedAccount(owner.Email))
                        return Results.BadRequest("This is a demo account. Its password cannot be changed.");

                    var displayName = string.IsNullOrWhiteSpace(owner.Name) ? owner.Email : owner.Name;
                    await invites.SendInviteAsync(owner.Email, displayName, InviteKind.Owner, ct);

                    return Results.NoContent();
                },
                logger,
                "resending an owner invite"))
            .RequireAuthorization()
            .WithName("ResendOwnerInvite");

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

                    var companyNames = companyIds.Count > 0
                        ? await db.Companies
                            .Where(c => companyIds.Contains(c.Id))
                            .ToDictionaryAsync(c => c.Id, c => c.Name, ct)
                        : new Dictionary<Guid, string>();

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

        // ── Admin: edit an owner's name and login email ──
        authApi.MapPut("/admin/owners/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, UpdateOwnerRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsAdmin())
                        return Results.Forbid();

                    if (string.IsNullOrWhiteSpace(request.Name))
                        return Results.BadRequest("Owner name is required.");

                    if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
                        return Results.BadRequest("A valid email address is required.");

                    var owner = await db.AppUsers.FirstOrDefaultAsync(x => x.Id == id && x.Role == WorkitRoles.Owner, ct);
                    if (owner is null)
                        return Results.NotFound();

                    if (DemoDataSeeder.IsProtectedAccount(owner.Email))
                        return Results.BadRequest("This is a demo account. It cannot be edited.");

                    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
                    if (normalizedEmail != owner.Email &&
                        await db.AppUsers.AnyAsync(x => x.Email == normalizedEmail && x.Id != id, ct))
                        return Results.Conflict("That email address is already in use.");

                    // The owner's Employee record is matched by email within the company,
                    // so it has to move with the login or the owner loses their own timesheet.
                    if (normalizedEmail != owner.Email && owner.CompanyId is { } companyId && companyId != Guid.Empty)
                    {
                        var employee = await db.Employees
                            .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.Email == owner.Email, ct);
                        if (employee is not null)
                            employee.Email = normalizedEmail;
                    }

                    owner.Name  = request.Name.Trim();
                    owner.Email = normalizedEmail;
                    await db.SaveChangesAsync(ct);

                    return Results.NoContent();
                },
                logger,
                "updating an owner account"))
            .RequireAuthorization()
            .WithName("UpdateOwner");

        // ── Admin: delete an owner account ──
        // Only the login is removed. Company records (jobs, time entries, invoices)
        // are business data and are deliberately left intact; an admin can still
        // reach an orphaned company from the Companies list.
        authApi.MapDelete("/admin/owners/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (!httpContext.User.IsAdmin())
                        return Results.Forbid();

                    var owner = await db.AppUsers.FirstOrDefaultAsync(x => x.Id == id && x.Role == WorkitRoles.Owner, ct);
                    if (owner is null)
                        return Results.NotFound();

                    if (DemoDataSeeder.IsProtectedAccount(owner.Email))
                        return Results.BadRequest("This is a demo account. It cannot be deleted.");

                    var companyLinks = await db.UserCompanies.Where(x => x.UserId == id).ToListAsync(ct);
                    db.UserCompanies.RemoveRange(companyLinks);

                    var refreshTokens = await db.RefreshTokens.Where(x => x.UserId == id).ToListAsync(ct);
                    db.RefreshTokens.RemoveRange(refreshTokens);

                    var resetTokens = await db.PasswordResetTokens.Where(x => x.Email == owner.Email).ToListAsync(ct);
                    db.PasswordResetTokens.RemoveRange(resetTokens);

                    db.AppUsers.Remove(owner);
                    await db.SaveChangesAsync(ct);

                    return Results.NoContent();
                },
                logger,
                "deleting an owner account"))
            .RequireAuthorization()
            .WithName("DeleteOwner");

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

                    var normalizedSsn = request.Company.Ssn.Trim();
                    if (await db.Companies.AnyAsync(x => x.Ssn == normalizedSsn, ct))
                        return Results.Conflict("A company with that SSN already exists.");

                    var company = new Company
                    {
                        Name               = request.Company.Name.Trim(),
                        Ssn                = request.Company.Ssn.Trim(),
                        Email              = request.Company.Email.Trim(),
                        Address            = request.Company.Address.Trim(),
                        Phone              = request.Company.Phone.Trim(),
                        Owner              = request.Company.Owner.Trim(),
                        ZipCode            = request.Company.ZipCode.Trim(),
                        City               = request.Company.City.Trim(),
                        VatNumber          = request.Company.VatNumber.Trim(),
                        Source             = request.Company.Source,
                        PaydayId           = request.Company.PaydayId,
                        PaydayClientId     = string.IsNullOrWhiteSpace(request.PaydayClientId)     ? null : credentialProtection.Protect(request.PaydayClientId.Trim()),
                        PaydayClientSecret = string.IsNullOrWhiteSpace(request.PaydayClientSecret) ? null : credentialProtection.Protect(request.PaydayClientSecret.Trim())
                    };

                    var user = await db.AppUsers.FirstOrDefaultAsync(x => x.Id == userContext.UserId, ct);
                    if (user is null)
                        return Results.Unauthorized();

                    user.CompanyId = company.Id;

                    var ownerEmployee = OwnerEmployeeHelper.CreateEmployeeForOwner(user, company.Id);

                    db.Companies.Add(company);
                    db.Employees.Add(ownerEmployee);
                    db.UserCompanies.Add(new UserCompany { UserId = user.Id, CompanyId = company.Id });
                    await db.SaveChangesAsync(ct);

                    // Issue a fresh token with the new company_id and employee_id embedded
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
                        Name      = request.Company.Name.Trim(),
                        Ssn       = request.Company.Ssn.Trim(),
                        Email     = request.Company.Email.Trim(),
                        Address   = request.Company.Address.Trim(),
                        Phone     = request.Company.Phone.Trim(),
                        Owner     = request.Company.Owner.Trim(),
                        ZipCode   = request.Company.ZipCode.Trim(),
                        City      = request.Company.City.Trim(),
                        VatNumber = request.Company.VatNumber.Trim()
                    };

                    db.Companies.Add(company);
                    var setupOwner = new AppUser
                    {
                        Email        = ownerEmail,
                        PasswordHash = PasswordHasher.HashPassword(password),
                        Role         = WorkitRoles.Owner,
                        CompanyId    = company.Id
                    };
                    var ownerEmployee = OwnerEmployeeHelper.CreateEmployeeForOwner(setupOwner, company.Id);

                    db.AppUsers.Add(setupOwner);
                    db.Employees.Add(ownerEmployee);
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

        // ── Signed-in user changes their own password ──
        authApi.MapPost("/change-password", async (WorkitDbContext db, HttpContext httpContext, TokenFactory tokenFactory, ChangePasswordRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (string.IsNullOrWhiteSpace(request.CurrentPassword))
                        return Results.BadRequest("Your current password is required.");

                    if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
                        return Results.BadRequest("Your new password must be at least 8 characters.");

                    if (string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal))
                        return Results.BadRequest("Your new password must be different from your current one.");

                    var userContext = httpContext.User.ToUserContext();
                    var user = await db.AppUsers.FirstOrDefaultAsync(x => x.Id == userContext.UserId, ct);
                    if (user is null)
                        return Results.NotFound();

                    if (!PasswordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
                        return Results.BadRequest("Your current password is not correct.");

                    if (DemoDataSeeder.IsProtectedAccount(user.Email))
                        return Results.BadRequest("This is a demo account. Its password cannot be changed.");

                    user.PasswordHash = PasswordHasher.HashPassword(request.NewPassword);

                    // Sign out every other device — a password change should end
                    // sessions that may have been opened with the old credentials.
                    var refreshTokens = await db.RefreshTokens
                        .Where(x => x.UserId == userContext.UserId && !x.Revoked)
                        .ToListAsync(ct);
                    foreach (var token in refreshTokens) token.Revoked = true;

                    // Re-issue for the caller so the device they just used stays signed in.
                    // The overrides preserve whichever company they are currently viewing.
                    var loginResponse = tokenFactory.CreateToken(user, userContext.CompanyId, userContext.EmployeeId);
                    var replacement = tokenFactory.CreateRefreshToken(user.Id);
                    db.RefreshTokens.Add(replacement);

                    await db.SaveChangesAsync(ct);
                    loginResponse.RefreshToken = replacement.Token;

                    return Results.Ok(loginResponse);
                },
                logger,
                "changing a password"))
            .RequireAuthorization()
            .WithName("ChangePassword");

        // ── Forgot password — always returns 200 to avoid email enumeration ──
        authApi.MapPost("/forgot-password", async (WorkitDbContext db, IEmailService emailService, IConfiguration config, ForgotPasswordRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return Results.BadRequest("Email is required.");

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var user = await db.AppUsers.FirstOrDefaultAsync(x => x.Email == normalizedEmail, ct);

            // Demo accounts never get a reset link. The response below is
            // deliberately identical either way so this does not leak which
            // addresses are registered.
            if (user is not null && !DemoDataSeeder.IsProtectedAccount(normalizedEmail))
            {
                // Expire any existing unused tokens for this email
                var existing = await db.PasswordResetTokens
                    .Where(t => t.Email == normalizedEmail && !t.Used && t.ExpiresAt > DateTime.UtcNow)
                    .ToListAsync(ct);
                foreach (var t in existing) t.Used = true;

                var rawToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                db.PasswordResetTokens.Add(new PasswordResetToken
                {
                    Email     = normalizedEmail,
                    Token     = rawToken,
                    ExpiresAt = DateTime.UtcNow.AddHours(1)
                });
                await db.SaveChangesAsync(ct);

                var appUrl = config["App:Url"]?.TrimEnd('/') ?? "https://app.workit.is";
                var resetUrl = $"{appUrl}/reset-password?token={rawToken}";
                await emailService.SendPasswordResetAsync(normalizedEmail, resetUrl);
            }

            return Results.Ok(new { message = "If that email is registered you will receive a reset link shortly." });
        })
        .WithName("ForgotPassword");

        // ── Redeem a password-reset token ──
        authApi.MapPost("/reset-password", async (WorkitDbContext db, RedeemPasswordResetRequest request, CancellationToken ct) =>
                await ExecuteDbAsync(async () =>
                {
                    if (string.IsNullOrWhiteSpace(request.Token))
                        return Results.BadRequest("Token is required.");

                    if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
                        return Results.BadRequest("Password must be at least 8 characters.");

                    var token = await db.PasswordResetTokens
                        .FirstOrDefaultAsync(t => t.Token == request.Token && !t.Used && t.ExpiresAt > DateTime.UtcNow, ct);

                    if (token is null)
                        return Results.BadRequest("This reset link is invalid or has expired.");

                    var user = await db.AppUsers.FirstOrDefaultAsync(x => x.Email == token.Email, ct);
                    if (user is null)
                        return Results.BadRequest("This reset link is invalid or has expired.");

                    // Tokens issued before this guard existed are still redeemable,
                    // so the demo check belongs here too, not only at issue time.
                    if (DemoDataSeeder.IsProtectedAccount(user.Email))
                        return Results.BadRequest("This is a demo account. Its password cannot be changed.");

                    user.PasswordHash = PasswordHasher.HashPassword(request.NewPassword);
                    token.Used = true;
                    await db.SaveChangesAsync(ct);

                    return Results.Ok(new { message = "Password updated successfully." });
                },
                logger,
                "redeeming a password reset token"))
            .WithName("RedeemPasswordReset");
    }
}

internal record SwitchCompanyRequest(Guid CompanyId);

internal static class OwnerEmployeeHelper
{
    /// <summary>
    /// Creates an Employee record for an owner in the given company and links it to their AppUser.
    /// </summary>
    internal static Employee CreateEmployeeForOwner(AppUser owner, Guid companyId)
    {
        var employee = new Employee
        {
            CompanyId = companyId,
            DisplayName = string.IsNullOrWhiteSpace(owner.Name) ? owner.Email : owner.Name,
            Email = owner.Email,
            EmploymentType = EmploymentType.Employed
        };
        owner.EmployeeId = employee.Id;
        return employee;
    }
}
