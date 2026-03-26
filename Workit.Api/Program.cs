using System.Data.Common;
using System.Text;
using System.Text.Json;
using Anthropic.SDK;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Services;
using Workit.Shared.Api;
using Workit.Shared.Auth;
using Workit.Shared.Models;
using Workit.Shared.Utilities;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: "Logs/workit-api-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        shared: true)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorApps", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            policy
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithOrigins(
                    "https://admin.workit.is",
                    "https://app.workit.is",
                    "https://localhost:7100",
                    "https://localhost:7300",
                    "https://localhost:7200",
                    "http://localhost:5100",
                    "http://localhost:5300",
                    "http://localhost:5200");
        }
    });
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<AdminSeedOptions>(builder.Configuration.GetSection(AdminSeedOptions.SectionName));
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<TokenFactory>();

builder.Services.AddDbContext<WorkitDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("WorkitDb")
        ?? "Host=localhost;Port=5432;Database=workkit;Username=postgres;Password=postgres";
    options.UseNpgsql(connectionString);
});

// ── Invoice scanning services ──────────────────────────────────────────────────
var anthropicApiKey = builder.Configuration["Anthropic:ApiKey"] ?? string.Empty;
builder.Services.AddSingleton(_ => new AnthropicClient(new Anthropic.SDK.APIAuthentication(anthropicApiKey)));
builder.Services.AddScoped<InvoiceParserService>();
builder.Services.AddScoped<EmailScanService>();
builder.Services.AddHostedService<InvoiceScanBackgroundService>();

// Payday API HttpClient (for credential testing)
builder.Services.AddHttpClient("PaydayApi", client =>
{
    client.BaseAddress = new Uri("https://api.payday.is/");
    client.DefaultRequestHeaders.Add("Api-Version", "alpha");
});

var app = builder.Build();
Microsoft.Extensions.Logging.ILogger apiLogger = app.Logger;

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowBlazorApps");
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");
var authApi = api.MapGroup("/auth");
var securedApi = api.MapGroup(string.Empty).RequireAuthorization();

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
        apiLogger,
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
        apiLogger,
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
        apiLogger,
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
        apiLogger,
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

            // Build owner lookup: companyId → owner email
            var ownerLookup = ownerUsers
                .GroupBy(x => x.CompanyId)
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
        apiLogger,
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
        apiLogger,
        "registering a company"))
    .RequireAuthorization()
    .WithName("RegisterCompany");

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
        apiLogger,
        "setting up company"))
    .RequireAuthorization()
    .WithName("SetupCompany");

securedApi.MapGet("/company", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            var userContext = httpContext.User.ToUserContext();
            var company = await db.Companies.FirstOrDefaultAsync(x => x.Id == userContext.CompanyId, ct);
            return company is null ? Results.NotFound() : Results.Ok(company);
        },
        apiLogger,
        "loading company"))
    .WithName("GetCompany");

securedApi.MapPost("/companies", async (WorkitDbContext db, HttpContext httpContext, Company request, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
                return Results.Forbid();

            var userContext = httpContext.User.ToUserContext();

            var company = new Company
            {
                Name = request.Name.Trim(),
                Ssn = request.Ssn.Trim(),
                Email = request.Email.Trim(),
                Address = request.Address.Trim(),
                Phone = request.Phone.Trim(),
                Owner = request.Owner.Trim()
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
        apiLogger,
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
        apiLogger,
        "updating driving rate"))
    .WithName("UpdateDrivingRate");

securedApi.MapGet("/customers", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
            {
                return Results.Forbid();
            }

            var userContext = httpContext.User.ToUserContext();
            var customers = await db.Customers
                .Where(x => x.CompanyId == userContext.CompanyId)
                .OrderBy(x => x.Name)
                .ToListAsync(ct);
            return Results.Ok(customers);
        },
        apiLogger,
        "loading customers"))
    .WithName("GetCustomers");

securedApi.MapPost("/customers", async (WorkitDbContext db, HttpContext httpContext, Customer customer, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
            {
                return Results.Forbid();
            }

            if (!IsValidCustomer(customer))
            {
                return Results.BadRequest("Customer name and SSN are required.");
            }

            customer.CompanyId = httpContext.User.ToUserContext().CompanyId;
            customer.Name = customer.Name.Trim();
            customer.Ssn = customer.Ssn.Trim();
            customer.Email = customer.Email.Trim();
            customer.Phone = customer.Phone.Trim();
            customer.ContactPerson = customer.ContactPerson.Trim();

            db.Customers.Add(customer);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/customers/{customer.Id}", customer);
        },
        apiLogger,
        "creating a customer"))
    .WithName("CreateCustomer");

securedApi.MapPut("/customers/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, Customer customer, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
            {
                return Results.Forbid();
            }

            if (id != customer.Id)
            {
                return Results.BadRequest("Customer id mismatch.");
            }

            if (!IsValidCustomer(customer))
            {
                return Results.BadRequest("Customer name and SSN are required.");
            }

            var userContext = httpContext.User.ToUserContext();
            var existing = await db.Customers.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
            if (existing is null)
            {
                return Results.NotFound();
            }

            existing.Name = customer.Name.Trim();
            existing.Ssn = customer.Ssn.Trim();
            existing.Email = customer.Email.Trim();
            existing.Phone = customer.Phone.Trim();
            existing.ContactPerson = customer.ContactPerson.Trim();

            await db.SaveChangesAsync(ct);
            return Results.Ok(existing);
        },
        apiLogger,
        "updating a customer"))
    .WithName("UpdateCustomer");

securedApi.MapGet("/employees", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            var userContext = httpContext.User.ToUserContext();

            IQueryable<Employee> query = db.Employees.Where(x => x.CompanyId == userContext.CompanyId);
            if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal))
            {
                if (userContext.EmployeeId is not Guid currentEmployeeId)
                {
                    return Results.Forbid();
                }

                query = query.Where(x => x.Id == currentEmployeeId);
            }
            else if (!string.Equals(userContext.Role, WorkitRoles.Owner, StringComparison.Ordinal)
                     && !string.Equals(userContext.Role, WorkitRoles.Admin, StringComparison.Ordinal))
            {
                return Results.Forbid();
            }

            var employees = await query.OrderBy(x => x.DisplayName).ToListAsync(ct);
            return Results.Ok(employees);
        },
        apiLogger,
        "loading employees"))
    .WithName("GetEmployees");

securedApi.MapPost("/employees", async (
        WorkitDbContext db,
        HttpContext httpContext,
        CreateEmployeeUserRequest request,
        CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
            {
                return Results.Forbid();
            }

            if (!IsValidEmployee(request.Employee))
            {
                return Results.BadRequest("Employee name, SSN, and email are required.");
            }

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            {
                return Results.BadRequest("Employee password must be at least 8 characters.");
            }

            var normalizedEmail = request.Employee.Email.Trim().ToLowerInvariant();
            if (await db.AppUsers.AnyAsync(x => x.Email == normalizedEmail, ct))
            {
                return Results.Conflict("That email address is already in use.");
            }

            var userContext = httpContext.User.ToUserContext();
            var employee = new Employee
            {
                CompanyId = userContext.CompanyId,
                DisplayName = request.Employee.DisplayName.Trim(),
                Trade = request.Employee.Trade.Trim(),
                Ssn = request.Employee.Ssn.Trim(),
                Email = normalizedEmail,
                Phone = request.Employee.Phone.Trim(),
                ContactPerson = request.Employee.ContactPerson.Trim()
            };

            var user = new AppUser
            {
                CompanyId = userContext.CompanyId,
                EmployeeId = employee.Id,
                Email = normalizedEmail,
                PasswordHash = PasswordHasher.HashPassword(request.Password),
                Role = WorkitRoles.Employee
            };

            db.Employees.Add(employee);
            db.AppUsers.Add(user);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/employees/{employee.Id}", employee);
        },
        apiLogger,
        "creating an employee"))
    .WithName("CreateEmployee");

securedApi.MapPut("/employees/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, Employee employee, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
            {
                return Results.Forbid();
            }

            if (id != employee.Id)
            {
                return Results.BadRequest("Employee id mismatch.");
            }

            if (!IsValidEmployee(employee))
            {
                return Results.BadRequest("Employee name, SSN, and email are required.");
            }

            var userContext = httpContext.User.ToUserContext();
            var existing = await db.Employees.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
            if (existing is null)
            {
                return Results.NotFound();
            }

            var normalizedEmail = employee.Email.Trim().ToLowerInvariant();
            if (await db.AppUsers.AnyAsync(x => x.Email == normalizedEmail && x.EmployeeId != id, ct))
            {
                return Results.Conflict("That email address is already in use.");
            }

            existing.DisplayName = employee.DisplayName.Trim();
            existing.Trade = employee.Trade.Trim();
            existing.Ssn = employee.Ssn.Trim();
            existing.Email = normalizedEmail;
            existing.Phone = employee.Phone.Trim();
            existing.ContactPerson = employee.ContactPerson.Trim();
            existing.EmploymentType = employee.EmploymentType;
            existing.HourlySalary = employee.HourlySalary;
            existing.HourlyBillableRate = employee.HourlyBillableRate;

            var appUser = await db.AppUsers.FirstOrDefaultAsync(x => x.EmployeeId == id, ct);
            if (appUser is not null)
            {
                appUser.Email = normalizedEmail;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(existing);
        },
        apiLogger,
        "updating an employee"))
    .WithName("UpdateEmployee");

securedApi.MapPut("/employees/{id:guid}/password", async (WorkitDbContext db, HttpContext httpContext, Guid id, ResetPasswordRequest request, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
            {
                return Results.Forbid();
            }

            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            {
                return Results.BadRequest("Password must be at least 8 characters.");
            }

            var userContext = httpContext.User.ToUserContext();
            var appUser = await db.AppUsers.FirstOrDefaultAsync(x => x.EmployeeId == id && x.CompanyId == userContext.CompanyId, ct);
            if (appUser is null)
            {
                return Results.NotFound();
            }

            appUser.PasswordHash = PasswordHasher.HashPassword(request.NewPassword);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        },
        apiLogger,
        "resetting employee password"))
    .WithName("ResetEmployeePassword");

securedApi.MapGet("/jobs", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            var userContext = httpContext.User.ToUserContext();
            var jobs = await db.Jobs
                .Where(x => x.CompanyId == userContext.CompanyId)
                .OrderBy(x => x.Code)
                .ToListAsync(ct);
            return Results.Ok(jobs);
        },
        apiLogger,
        "loading jobs"))
    .WithName("GetJobs");

securedApi.MapPost("/jobs", async (WorkitDbContext db, HttpContext httpContext, Job job, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
            {
                return Results.Forbid();
            }

            var userContext = httpContext.User.ToUserContext();
            job.CompanyId = userContext.CompanyId;
            job.Code = job.Code.Trim();
            job.Name = job.Name.Trim();

            db.Jobs.Add(job);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/jobs/{job.Id}", job);
        },
        apiLogger,
        "creating a job"))
    .WithName("CreateJob");

securedApi.MapPut("/jobs/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, Job job, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
            {
                return Results.Forbid();
            }

            if (id != job.Id)
            {
                return Results.BadRequest("Job id mismatch.");
            }

            if (string.IsNullOrWhiteSpace(job.Name) || string.IsNullOrWhiteSpace(job.Code))
            {
                return Results.BadRequest("Job name and code are required.");
            }

            var userContext = httpContext.User.ToUserContext();
            var existing = await db.Jobs.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
            if (existing is null)
            {
                return Results.NotFound();
            }

            existing.CustomerId  = job.CustomerId;
            existing.Code        = job.Code.Trim();
            existing.Name        = job.Name.Trim();
            existing.BillingType = job.BillingType;

            await db.SaveChangesAsync(ct);
            return Results.Ok(existing);
        },
        apiLogger,
        "updating a job"))
    .WithName("UpdateJob");

securedApi.MapGet("/timeentries", async (
        WorkitDbContext db,
        HttpContext httpContext,
        Guid? employeeId,
        Guid? jobId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct) =>
    {
        var userContext = httpContext.User.ToUserContext();
        var query = db.TimeEntries.Where(x => x.CompanyId == userContext.CompanyId);

        if (string.Equals(userContext.Role, WorkitRoles.Owner, StringComparison.Ordinal)
            || string.Equals(userContext.Role, WorkitRoles.Admin, StringComparison.Ordinal))
        {
            if (employeeId is not null)
            {
                query = query.Where(x => x.EmployeeId == employeeId.Value);
            }
        }
        else if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal))
        {
            if (userContext.EmployeeId is not Guid currentEmployeeId)
            {
                return Results.Forbid();
            }

            query = query.Where(x => x.EmployeeId == currentEmployeeId);
        }
        else
        {
            return Results.Forbid();
        }

        if (jobId is not null)
        {
            query = query.Where(x => x.JobId == jobId.Value);
        }

        if (from is not null)
        {
            query = query.Where(x => x.WorkDate >= from.Value);
        }

        if (to is not null)
        {
            query = query.Where(x => x.WorkDate <= to.Value);
        }

        return await ExecuteDbAsync(
            async () => Results.Ok(await query.OrderByDescending(x => x.WorkDate).ToListAsync(ct)),
            apiLogger,
            "loading time entries");
    })
    .WithName("GetTimeEntries");

securedApi.MapPost("/timeentries", async (WorkitDbContext db, HttpContext httpContext, TimeEntry entry, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            var userContext = httpContext.User.ToUserContext();

            if (string.Equals(userContext.Role, WorkitRoles.Owner, StringComparison.Ordinal)
                || string.Equals(userContext.Role, WorkitRoles.Admin, StringComparison.Ordinal))
            {
                // Owner/Admin posts on behalf of any employee in their company.
                entry.CompanyId = userContext.CompanyId;
                var employeeExists = await db.Employees
                    .AnyAsync(x => x.Id == entry.EmployeeId && x.CompanyId == userContext.CompanyId, ct);
                if (!employeeExists)
                    return Results.BadRequest("Employee not found in this company.");
            }
            else if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal) &&
                     userContext.EmployeeId is Guid currentEmployeeId)
            {
                entry.CompanyId = userContext.CompanyId;
                entry.EmployeeId = currentEmployeeId;
            }
            else
            {
                return Results.Forbid();
            }

            db.TimeEntries.Add(entry);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/timeentries/{entry.Id}", entry);
        },
        apiLogger,
        "creating a time entry"))
    .WithName("CreateTimeEntry");

securedApi.MapPut("/timeentries/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, TimeEntry entry, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (id != entry.Id)
                return Results.BadRequest("Time entry id mismatch.");

            var userContext = httpContext.User.ToUserContext();
            var existing = await db.TimeEntries.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
            if (existing is null) return Results.NotFound();

            // Employees can only edit their own entries
            if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal) &&
                existing.EmployeeId != userContext.EmployeeId)
                return Results.Forbid();

            existing.JobId         = entry.JobId;
            existing.WorkDate      = entry.WorkDate;
            existing.Hours         = entry.Hours;
            existing.OvertimeHours = entry.OvertimeHours;
            existing.DrivingUnits  = entry.DrivingUnits;
            existing.Notes         = entry.Notes;
            existing.IsInvoiced          = entry.IsInvoiced;
            existing.InvoicedAt          = entry.InvoicedAt;
            existing.PaydayInvoiceNumber = entry.PaydayInvoiceNumber;

            await db.SaveChangesAsync(ct);
            return Results.Ok(existing);
        },
        apiLogger,
        "updating a time entry"))
    .WithName("UpdateTimeEntry");

securedApi.MapPost("/timeentries/mark-invoiced", async (WorkitDbContext db, HttpContext httpContext, MarkInvoicedRequest request, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
                return Results.Forbid();

            var userContext = httpContext.User.ToUserContext();

            if (request.Ids is null || request.Ids.Count == 0)
                return Results.BadRequest("No time entry ids provided.");

            var entries = await db.TimeEntries
                .Where(x => x.CompanyId == userContext.CompanyId && request.Ids.Contains(x.Id))
                .ToListAsync(ct);

            var now = DateTimeOffset.UtcNow;
            foreach (var entry in entries)
            {
                entry.IsInvoiced = true;
                entry.InvoicedAt = now;
                entry.PaydayInvoiceNumber = request.PaydayInvoiceNumber;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Marked = entries.Count });
        },
        apiLogger,
        "marking time entries as invoiced"))
    .WithName("MarkTimeEntriesInvoiced");

securedApi.MapPost("/timeentries/mark-uninvoiced", async (WorkitDbContext db, HttpContext httpContext, MarkUninvoicedRequest request, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
                return Results.Forbid();

            var userContext = httpContext.User.ToUserContext();

            var entries = await db.TimeEntries
                .Where(x => x.CompanyId == userContext.CompanyId
                          && x.IsInvoiced
                          && x.PaydayInvoiceNumber == request.PaydayInvoiceNumber)
                .ToListAsync(ct);

            foreach (var entry in entries)
            {
                entry.IsInvoiced = false;
                entry.InvoicedAt = null;
                entry.PaydayInvoiceNumber = null;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Reset = entries.Count });
        },
        apiLogger,
        "unmarking time entries as invoiced"))
    .WithName("MarkTimeEntriesUninvoiced");

securedApi.MapPost("/materials/usage/mark-invoiced", async (WorkitDbContext db, HttpContext httpContext, MarkInvoicedRequest request, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
                return Results.Forbid();

            var userContext = httpContext.User.ToUserContext();

            if (request.Ids is null || request.Ids.Count == 0)
                return Results.BadRequest("No material usage ids provided.");

            var usages = await db.MaterialUsages
                .Where(x => x.CompanyId == userContext.CompanyId && request.Ids.Contains(x.Id))
                .ToListAsync(ct);

            var now = DateTimeOffset.UtcNow;
            foreach (var usage in usages)
            {
                usage.IsInvoiced = true;
                usage.InvoicedAt = now;
                usage.PaydayInvoiceNumber = request.PaydayInvoiceNumber;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Marked = usages.Count });
        },
        apiLogger,
        "marking material usage as invoiced"))
    .WithName("MarkMaterialUsageInvoiced");

securedApi.MapPost("/materials/usage/mark-uninvoiced", async (WorkitDbContext db, HttpContext httpContext, MarkUninvoicedRequest request, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
                return Results.Forbid();

            var userContext = httpContext.User.ToUserContext();

            var usages = await db.MaterialUsages
                .Where(x => x.CompanyId == userContext.CompanyId
                          && x.IsInvoiced
                          && x.PaydayInvoiceNumber == request.PaydayInvoiceNumber)
                .ToListAsync(ct);

            foreach (var usage in usages)
            {
                usage.IsInvoiced = false;
                usage.InvoicedAt = null;
                usage.PaydayInvoiceNumber = null;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Reset = usages.Count });
        },
        apiLogger,
        "unmarking material usage as invoiced"))
    .WithName("MarkMaterialUsageUninvoiced");

// ── Tools ─────────────────────────────────────────────────────────────────────

securedApi.MapGet("/tools", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            var userContext = httpContext.User.ToUserContext();
            var tools = await db.Tools
                .Where(x => x.CompanyId == userContext.CompanyId)
                .OrderBy(x => x.Name)
                .ToListAsync(ct);
            return Results.Ok(tools);
        },
        apiLogger,
        "loading tools"))
    .WithName("GetTools");

securedApi.MapPost("/tools", async (WorkitDbContext db, HttpContext httpContext, Tool tool, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
            {
                return Results.Forbid();
            }

            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                return Results.BadRequest("Tool name is required.");
            }

            var userContext = httpContext.User.ToUserContext();
            tool.CompanyId = userContext.CompanyId;
            tool.Name = tool.Name.Trim();
            tool.Description = tool.Description.Trim();
            tool.SerialNumber = tool.SerialNumber.Trim();

            db.Tools.Add(tool);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/tools/{tool.Id}", tool);
        },
        apiLogger,
        "creating a tool"))
    .WithName("CreateTool");

securedApi.MapPut("/tools/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, Tool tool, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
            {
                return Results.Forbid();
            }

            if (id != tool.Id)
            {
                return Results.BadRequest("Tool id mismatch.");
            }

            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                return Results.BadRequest("Tool name is required.");
            }

            var userContext = httpContext.User.ToUserContext();
            var existing = await db.Tools.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
            if (existing is null)
            {
                return Results.NotFound();
            }

            existing.Name = tool.Name.Trim();
            existing.Description = tool.Description.Trim();
            existing.SerialNumber = tool.SerialNumber.Trim();

            await db.SaveChangesAsync(ct);
            return Results.Ok(existing);
        },
        apiLogger,
        "updating a tool"))
    .WithName("UpdateTool");

securedApi.MapDelete("/tools/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
            {
                return Results.Forbid();
            }

            var userContext = httpContext.User.ToUserContext();
            var existing = await db.Tools.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
            if (existing is null)
            {
                return Results.NotFound();
            }

            // Check if tool is currently assigned
            var isAssigned = await db.ToolAssignments
                .AnyAsync(x => x.ToolId == id && x.ReturnedAt == null, ct);
            if (isAssigned)
            {
                return Results.BadRequest("Cannot delete a tool that is currently assigned to an employee.");
            }

            db.Tools.Remove(existing);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        },
        apiLogger,
        "deleting a tool"))
    .WithName("DeleteTool");

// ── Tool Assignments ───────────────────────────────────────────────────────────

securedApi.MapGet("/tools/assignments", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            var userContext = httpContext.User.ToUserContext();

            IQueryable<ToolAssignment> query = db.ToolAssignments
                .Where(x => x.CompanyId == userContext.CompanyId);

            if (httpContext.User.IsOwnerOrAdmin())
            {
                // Owner sees all assignments (full history)
                query = query.OrderByDescending(x => x.AssignedAt);
            }
            else if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal))
            {
                // Employee sees only active (unreturned) assignments so they can see who has what
                query = query.Where(x => x.ReturnedAt == null).OrderBy(x => x.ToolId);
            }
            else
            {
                return Results.Forbid();
            }

            var assignments = await query.ToListAsync(ct);
            return Results.Ok(assignments);
        },
        apiLogger,
        "loading tool assignments"))
    .WithName("GetToolAssignments");

securedApi.MapPost("/tools/{id:guid}/assign", async (WorkitDbContext db, HttpContext httpContext, Guid id, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            var userContext = httpContext.User.ToUserContext();

            if (userContext.EmployeeId is not Guid currentEmployeeId)
            {
                return Results.Forbid();
            }

            var tool = await db.Tools.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
            if (tool is null)
            {
                return Results.NotFound();
            }

            // Check tool is not already assigned
            var alreadyAssigned = await db.ToolAssignments
                .AnyAsync(x => x.ToolId == id && x.ReturnedAt == null, ct);
            if (alreadyAssigned)
            {
                return Results.BadRequest("This tool is already assigned to another employee.");
            }

            var assignment = new ToolAssignment
            {
                ToolId = id,
                CompanyId = userContext.CompanyId,
                EmployeeId = currentEmployeeId,
                AssignedAt = DateTimeOffset.UtcNow
            };

            db.ToolAssignments.Add(assignment);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/tools/{id}/assignments", assignment);
        },
        apiLogger,
        "assigning a tool"))
    .WithName("AssignTool");

securedApi.MapPost("/tools/{id:guid}/return", async (WorkitDbContext db, HttpContext httpContext, Guid id, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            var userContext = httpContext.User.ToUserContext();

            if (userContext.EmployeeId is not Guid currentEmployeeId)
            {
                // Owners can return tools too
                if (!httpContext.User.IsOwnerOrAdmin())
                {
                    return Results.Forbid();
                }

                // Owner: return current assignment regardless of employee
                var ownerAssignment = await db.ToolAssignments
                    .FirstOrDefaultAsync(x => x.ToolId == id && x.CompanyId == userContext.CompanyId && x.ReturnedAt == null, ct);
                if (ownerAssignment is null)
                {
                    return Results.NotFound();
                }

                ownerAssignment.ReturnedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return Results.Ok(ownerAssignment);
            }

            var assignment = await db.ToolAssignments
                .FirstOrDefaultAsync(x => x.ToolId == id && x.EmployeeId == currentEmployeeId && x.ReturnedAt == null, ct);
            if (assignment is null)
            {
                return Results.NotFound();
            }

            assignment.ReturnedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(assignment);
        },
        apiLogger,
        "returning a tool"))
    .WithName("ReturnTool");

// ── Materials ──────────────────────────────────────────────────────────────────

securedApi.MapGet("/materials", async (WorkitDbContext db, HttpContext httpContext, string? category, bool? activeOnly, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            var userContext = httpContext.User.ToUserContext();
            var query = db.Materials.Where(x => x.CompanyId == userContext.CompanyId);

            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(x => x.Category == category);

            if (activeOnly == true)
                query = query.Where(x => x.IsActive);

            var materials = await query.OrderBy(x => x.Category).ThenBy(x => x.Name).ToListAsync(ct);
            return Results.Ok(materials);
        },
        apiLogger,
        "loading materials"))
    .WithName("GetMaterials");

securedApi.MapPost("/materials", async (WorkitDbContext db, HttpContext httpContext, Material material, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
                return Results.Forbid();

            if (string.IsNullOrWhiteSpace(material.Name))
                return Results.BadRequest("Material name is required.");

            var userContext = httpContext.User.ToUserContext();
            material.CompanyId   = userContext.CompanyId;
            material.Name        = material.Name.Trim();
            material.ProductCode = material.ProductCode.Trim();
            material.Category    = material.Category.Trim();
            material.Description = material.Description.Trim();
            material.Unit        = material.Unit.Trim();

            db.Materials.Add(material);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/materials/{material.Id}", material);
        },
        apiLogger,
        "creating a material"))
    .WithName("CreateMaterial");

securedApi.MapPut("/materials/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, Material material, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
                return Results.Forbid();

            if (id != material.Id)
                return Results.BadRequest("Material id mismatch.");

            if (string.IsNullOrWhiteSpace(material.Name))
                return Results.BadRequest("Material name is required.");

            var userContext = httpContext.User.ToUserContext();
            var existing = await db.Materials.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
            if (existing is null)
                return Results.NotFound();

            existing.Name          = material.Name.Trim();
            existing.ProductCode   = material.ProductCode.Trim();
            existing.Category      = material.Category.Trim();
            existing.Unit          = material.Unit.Trim();
            existing.PurchasePrice = material.PurchasePrice;
            existing.MarkupFactor  = material.MarkupFactor > 0 ? material.MarkupFactor : 1.5m;
            existing.UnitPrice     = material.UnitPrice;
            existing.VatRate       = material.VatRate;
            existing.Quantity      = material.Quantity;
            existing.Description   = material.Description.Trim();
            existing.IsActive      = material.IsActive;

            await db.SaveChangesAsync(ct);
            return Results.Ok(existing);
        },
        apiLogger,
        "updating a material"))
    .WithName("UpdateMaterial");

securedApi.MapDelete("/materials/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
                return Results.Forbid();

            var userContext = httpContext.User.ToUserContext();
            var existing = await db.Materials.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
            if (existing is null)
                return Results.NotFound();

            db.Materials.Remove(existing);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        },
        apiLogger,
        "deleting a material"))
    .WithName("DeleteMaterial");

// ── Material Usage ─────────────────────────────────────────────────────────────

securedApi.MapGet("/materials/usage", async (WorkitDbContext db, HttpContext httpContext, Guid? jobId, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            var userContext = httpContext.User.ToUserContext();
            var query = db.MaterialUsages.Where(x => x.CompanyId == userContext.CompanyId);

            if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal))
            {
                if (userContext.EmployeeId is not Guid currentEmployeeId)
                    return Results.Forbid();
                query = query.Where(x => x.EmployeeId == currentEmployeeId);
            }

            if (jobId is not null)
                query = query.Where(x => x.JobId == jobId);

            var usage = await query.OrderByDescending(x => x.UsedAt).ToListAsync(ct);
            return Results.Ok(usage);
        },
        apiLogger,
        "loading material usage"))
    .WithName("GetMaterialUsage");

securedApi.MapPost("/materials/usage", async (WorkitDbContext db, HttpContext httpContext, MaterialUsage usage, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            var userContext = httpContext.User.ToUserContext();

            if (string.Equals(userContext.Role, WorkitRoles.Owner, StringComparison.Ordinal)
                || string.Equals(userContext.Role, WorkitRoles.Admin, StringComparison.Ordinal))
            {
                usage.CompanyId = userContext.CompanyId;
                var employeeExists = await db.Employees.AnyAsync(x => x.Id == usage.EmployeeId && x.CompanyId == userContext.CompanyId, ct);
                if (!employeeExists)
                    return Results.BadRequest("Employee not found in this company.");
            }
            else if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal) &&
                     userContext.EmployeeId is Guid currentEmployeeId)
            {
                usage.CompanyId  = userContext.CompanyId;
                usage.EmployeeId = currentEmployeeId;
            }
            else
            {
                return Results.Forbid();
            }

            var material = await db.Materials.FirstOrDefaultAsync(x => x.Id == usage.MaterialId && x.CompanyId == userContext.CompanyId, ct);
            if (material is null)
                return Results.BadRequest("Material not found.");

            if (usage.Quantity <= 0)
                return Results.BadRequest("Quantity must be greater than zero.");

            // Deduct from stock
            material.Quantity = Math.Max(0, material.Quantity - usage.Quantity);

            usage.UsedAt = DateTimeOffset.UtcNow;

            db.MaterialUsages.Add(usage);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/materials/usage/{usage.Id}", usage);
        },
        apiLogger,
        "logging material usage"))
    .WithName("CreateMaterialUsage");

// ── Email Settings ─────────────────────────────────────────────────────────────

securedApi.MapGet("/email-settings", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
            var userContext = httpContext.User.ToUserContext();
            var settings = await db.EmailSettings.FirstOrDefaultAsync(x => x.CompanyId == userContext.CompanyId, ct);
            return settings is null ? Results.NotFound() : Results.Ok(settings);
        }, apiLogger, "loading email settings"))
    .WithName("GetEmailSettings");

securedApi.MapPut("/email-settings", async (WorkitDbContext db, HttpContext httpContext, EmailSettings incoming, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
            var userContext = httpContext.User.ToUserContext();
            var existing = await db.EmailSettings.FirstOrDefaultAsync(x => x.CompanyId == userContext.CompanyId, ct);

            if (existing is null)
            {
                incoming.CompanyId = userContext.CompanyId;
                db.EmailSettings.Add(incoming);
            }
            else
            {
                existing.ImapHost        = incoming.ImapHost.Trim();
                existing.ImapPort        = incoming.ImapPort;
                existing.UseSsl          = incoming.UseSsl;
                existing.Username        = incoming.Username.Trim();
                if (!string.IsNullOrWhiteSpace(incoming.Password))
                    existing.Password    = incoming.Password;
                existing.InvoiceFolder   = incoming.InvoiceFolder.Trim();
                existing.AutoScanEnabled = incoming.AutoScanEnabled;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(existing ?? incoming);
        }, apiLogger, "saving email settings"))
    .WithName("SaveEmailSettings");

securedApi.MapPost("/email-settings/test", async (WorkitDbContext db, HttpContext httpContext, EmailSettings testSettings, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
            try
            {
                await EmailScanService.TestConnectionAsync(testSettings);
                return Results.Ok(new { ok = true, message = "Connection successful." });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { ok = false, message = ex.Message });
            }
        }, apiLogger, "testing email connection"))
    .WithName("TestEmailConnection");

// ── Vendor Invoices ────────────────────────────────────────────────────────────

securedApi.MapPost("/invoices/scan", async (
        WorkitDbContext db,
        HttpContext httpContext,
        EmailScanService emailService,
        CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
            var userContext = httpContext.User.ToUserContext();
            var settings = await db.EmailSettings.FirstOrDefaultAsync(x => x.CompanyId == userContext.CompanyId, ct);
            if (settings is null)
                return Results.BadRequest("Email settings not configured. Please configure IMAP settings first.");

            var result = await emailService.ScanAsync(settings, userContext.CompanyId);
            return Results.Ok(result);
        }, apiLogger, "scanning emails for invoices"))
    .WithName("ScanInvoices");

if (app.Environment.IsDevelopment())
{
    securedApi.MapPost("/dev/seed-test-data", async (
            WorkitDbContext db,
            HttpContext httpContext,
            Guid? companyId,
            CancellationToken ct) =>
            await ExecuteDbAsync(async () =>
            {
                // In dev, admin can seed any company. Owner seeds their own.
                var userContext = httpContext.User.ToUserContext();
                var targetCompanyId = companyId ?? userContext.CompanyId;

                if (targetCompanyId == Guid.Empty)
                {
                    // Auto-pick first company if admin has no company
                    var first = await db.Companies.FirstOrDefaultAsync(ct);
                    if (first is null) return Results.BadRequest("No companies exist. Create one first.");
                    targetCompanyId = first.Id;
                }

                var result = await TestDataSeeder.SeedFebruaryAsync(db, targetCompanyId);
                return Results.Ok(result);
            }, apiLogger, "seeding test data"))
        .WithName("SeedTestData");

    securedApi.MapPost("/invoices/scan-folder", async (
            WorkitDbContext db,
            HttpContext httpContext,
            EmailScanService emailService,
            IConfiguration config,
            CancellationToken ct) =>
            await ExecuteDbAsync(async () =>
            {
                if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
                var userContext = httpContext.User.ToUserContext();
                var folder = config["DevScanFolder"] ?? "TestInvoices";
                var result = await emailService.ScanFolderAsync(folder, userContext.CompanyId);
                return Results.Ok(result);
            }, apiLogger, "scanning local folder for invoices"))
        .WithName("ScanInvoicesFromFolder");
}

securedApi.MapGet("/invoices", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
            var userContext = httpContext.User.ToUserContext();
            var invoices = await db.VendorInvoices
                .Where(x => x.CompanyId == userContext.CompanyId)
                .OrderByDescending(x => x.InvoiceDate)
                .ToListAsync(ct);
            return Results.Ok(invoices);
        }, apiLogger, "loading vendor invoices"))
    .WithName("GetInvoices");

securedApi.MapGet("/invoices/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin()) return Results.Forbid();
            var userContext = httpContext.User.ToUserContext();
            var invoice = await db.VendorInvoices.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
            if (invoice is null) return Results.NotFound();

            invoice.LineItems = await db.VendorInvoiceLineItems
                .Where(x => x.InvoiceId == id)
                .OrderBy(x => x.Description)
                .ToListAsync(ct);

            return Results.Ok(invoice);
        }, apiLogger, "loading vendor invoice detail"))
    .WithName("GetInvoice");

// ── Absences ──────────────────────────────────────────────────────────────────

securedApi.MapGet("/absences", async (
        WorkitDbContext db,
        HttpContext httpContext,
        Guid? employeeId,
        DateOnly? from,
        DateOnly? to,
        AbsenceStatus? status,
        CancellationToken ct) =>
    {
        var userContext = httpContext.User.ToUserContext();
        var query = db.AbsenceRequests.Where(x => x.CompanyId == userContext.CompanyId);

        if (string.Equals(userContext.Role, WorkitRoles.Owner, StringComparison.Ordinal)
            || string.Equals(userContext.Role, WorkitRoles.Admin, StringComparison.Ordinal))
        {
            if (employeeId is not null)
                query = query.Where(x => x.EmployeeId == employeeId.Value);
        }
        else if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal))
        {
            if (userContext.EmployeeId is not Guid currentEmployeeId)
                return Results.Forbid();
            query = query.Where(x => x.EmployeeId == currentEmployeeId);
        }
        else
        {
            return Results.Forbid();
        }

        if (from is not null) query = query.Where(x => x.StartDate >= from.Value);
        if (to is not null) query = query.Where(x => x.EndDate <= to.Value);
        if (status is not null) query = query.Where(x => x.Status == status.Value);

        return await ExecuteDbAsync(
            async () => Results.Ok(await query.OrderByDescending(x => x.StartDate).ToListAsync(ct)),
            apiLogger,
            "loading absences");
    })
    .WithName("GetAbsences");

securedApi.MapPost("/absences", async (WorkitDbContext db, HttpContext httpContext, AbsenceRequest absence, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            var userContext = httpContext.User.ToUserContext();

            if (absence.StartDate > absence.EndDate)
                return Results.BadRequest("Start date must be before or equal to end date.");

            if (string.Equals(userContext.Role, WorkitRoles.Owner, StringComparison.Ordinal)
                || string.Equals(userContext.Role, WorkitRoles.Admin, StringComparison.Ordinal))
            {
                // Owner/Admin registers absence directly — auto-approved
                absence.CompanyId = userContext.CompanyId;
                absence.Status = AbsenceStatus.Approved;
                absence.ReviewedBy = userContext.UserId;
                absence.ReviewedAt = DateTime.UtcNow;

                var employeeExists = await db.Employees
                    .AnyAsync(x => x.Id == absence.EmployeeId && x.CompanyId == userContext.CompanyId, ct);
                if (!employeeExists)
                    return Results.BadRequest("Employee not found in this company.");
            }
            else if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal) &&
                     userContext.EmployeeId is Guid currentEmployeeId)
            {
                // Employee requests absence — starts as Pending
                absence.CompanyId = userContext.CompanyId;
                absence.EmployeeId = currentEmployeeId;
                absence.Status = AbsenceStatus.Pending;
                absence.ReviewedBy = null;
                absence.ReviewedAt = null;
            }
            else
            {
                return Results.Forbid();
            }

            absence.CreatedAt = DateTime.UtcNow;
            db.AbsenceRequests.Add(absence);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/absences/{absence.Id}", absence);
        },
        apiLogger,
        "creating an absence"))
    .WithName("CreateAbsence");

securedApi.MapPut("/absences/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, AbsenceRequest absence, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (id != absence.Id)
                return Results.BadRequest("Absence id mismatch.");

            var userContext = httpContext.User.ToUserContext();
            var existing = await db.AbsenceRequests.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
            if (existing is null) return Results.NotFound();

            // Only pending absences can be edited
            if (existing.Status != AbsenceStatus.Pending && !httpContext.User.IsOwnerOrAdmin())
                return Results.BadRequest("Only pending absences can be edited.");

            // Employees can only edit their own
            if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal) &&
                existing.EmployeeId != userContext.EmployeeId)
                return Results.Forbid();

            existing.Type = absence.Type;
            existing.StartDate = absence.StartDate;
            existing.EndDate = absence.EndDate;
            existing.Notes = absence.Notes;

            await db.SaveChangesAsync(ct);
            return Results.Ok(existing);
        },
        apiLogger,
        "updating an absence"))
    .WithName("UpdateAbsence");

securedApi.MapDelete("/absences/{id:guid}", async (WorkitDbContext db, HttpContext httpContext, Guid id, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            var userContext = httpContext.User.ToUserContext();
            var existing = await db.AbsenceRequests.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
            if (existing is null) return Results.NotFound();

            if (string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal))
            {
                // Employees can only cancel their own pending requests
                if (existing.EmployeeId != userContext.EmployeeId)
                    return Results.Forbid();
                if (existing.Status != AbsenceStatus.Pending)
                    return Results.BadRequest("Only pending requests can be cancelled.");

                existing.Status = AbsenceStatus.Cancelled;
                await db.SaveChangesAsync(ct);
                return Results.Ok(existing);
            }

            if (!httpContext.User.IsOwnerOrAdmin())
                return Results.Forbid();

            db.AbsenceRequests.Remove(existing);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        },
        apiLogger,
        "deleting an absence"))
    .WithName("DeleteAbsence");

securedApi.MapPost("/absences/{id:guid}/review", async (WorkitDbContext db, HttpContext httpContext, Guid id, AbsenceReviewPayload payload, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwnerOrAdmin())
                return Results.Forbid();

            var userContext = httpContext.User.ToUserContext();
            var existing = await db.AbsenceRequests.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == userContext.CompanyId, ct);
            if (existing is null) return Results.NotFound();

            if (existing.Status != AbsenceStatus.Pending)
                return Results.BadRequest("Only pending absences can be reviewed.");

            if (payload.Status != AbsenceStatus.Approved && payload.Status != AbsenceStatus.Denied)
                return Results.BadRequest("Status must be Approved or Denied.");

            existing.Status = payload.Status;
            existing.ReviewedBy = userContext.UserId;
            existing.ReviewedAt = DateTime.UtcNow;
            existing.ReviewNotes = payload.ReviewNotes ?? string.Empty;

            await db.SaveChangesAsync(ct);
            return Results.Ok(existing);
        },
        apiLogger,
        "reviewing an absence"))
    .WithName("ReviewAbsence");

// ── Work Duty ──────────────────────────────────────────────
app.MapGet("/api/workduty", async (
    int year,
    int month,
    WorkitDbContext db,
    HttpContext httpContext) =>
{
    var userContext = httpContext.User.ToUserContext();

    // Get company for standard hours
    var company = await db.Companies.FindAsync(userContext.CompanyId);
    var standardHours = company?.StandardHoursPerDay ?? 8m;

    // Calculate work duty
    var holidays = IcelandicHolidays.GetHolidaysInMonth(year, month);
    var dutyHours = IcelandicHolidays.GetWorkDutyHours(year, month, standardHours);

    // Get hours worked for the authenticated employee
    var startDate = new DateOnly(year, month, 1);
    var endDate = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

    decimal hoursWorked = 0;
    if (userContext.EmployeeId is Guid empId)
    {
        hoursWorked = await db.TimeEntries
            .Where(t => t.CompanyId == userContext.CompanyId
                     && t.EmployeeId == empId
                     && t.WorkDate >= startDate
                     && t.WorkDate <= endDate)
            .SumAsync(t => t.Hours + t.OvertimeHours);
    }

    // Count weekdays, holidays
    var daysInMonth = DateTime.DaysInMonth(year, month);
    int weekdays = 0;
    for (int d = 1; d <= daysInMonth; d++)
    {
        var dow = new DateOnly(year, month, d).DayOfWeek;
        if (dow != DayOfWeek.Saturday && dow != DayOfWeek.Sunday) weekdays++;
    }

    var fullHolidays = holidays.Count(h => !h.IsHalfDay && h.Date.DayOfWeek != DayOfWeek.Saturday && h.Date.DayOfWeek != DayOfWeek.Sunday);
    var halfHolidays = holidays.Count(h => h.IsHalfDay && h.Date.DayOfWeek != DayOfWeek.Saturday && h.Date.DayOfWeek != DayOfWeek.Sunday);

    var remaining = Math.Max(0, dutyHours - hoursWorked);
    var pct = dutyHours > 0 ? Math.Round(hoursWorked / dutyHours * 100, 1) : 0;

    return Results.Ok(new WorkDutyResponse
    {
        Year = year,
        Month = month,
        TotalCalendarDays = daysInMonth,
        WeekdaysInMonth = weekdays,
        FullHolidays = fullHolidays,
        HalfHolidays = halfHolidays,
        WorkDutyHours = dutyHours,
        HoursWorked = hoursWorked,
        HoursRemaining = remaining,
        CompletionPercentage = pct,
        Holidays = holidays.Select(h => new HolidayInfo
        {
            Date = h.Date.ToString("yyyy-MM-dd"),
            Name = h.Name,
            IsHalfDay = h.IsHalfDay
        }).ToList()
    });
}).RequireAuthorization();

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
}).RequireAuthorization();

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

    company.PaydayClientId = string.IsNullOrWhiteSpace(req.ClientId) ? null : req.ClientId.Trim();
    company.PaydayClientSecret = string.IsNullOrWhiteSpace(req.ClientSecret) ? null : req.ClientSecret.Trim();
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

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
            var companySsn = companyData.TryGetProperty("ssn", out var ssnProp) ? ssnProp.GetString() : "";
            return Results.Json(new { success = true, message = $"Connected to Payday company: {companyName} ({companySsn})" });
        }

        return Results.Json(new { success = true, message = "Authentication successful, but could not fetch company details." });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, message = $"Connection error: {ex.Message}" });
    }
}).RequireAuthorization();

api.MapGet("/status/database", async (WorkitDbContext db, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            var canConnect = await db.Database.CanConnectAsync(ct);
            return Results.Ok(new
            {
                databaseAvailable = canConnect,
                message = canConnect ? "Database connection is available." : "Database connection is unavailable."
            });
        },
        apiLogger,
        "checking database availability"))
    .WithName("GetDatabaseStatus");

var isDesignTime = string.Equals(Environment.GetEnvironmentVariable("EF_DESIGN_TIME"), "true", StringComparison.OrdinalIgnoreCase);

if (!isDesignTime)
{
    await LogStartupDatabaseStatusAsync(app.Services, apiLogger);
}

if (!isDesignTime)
{
    var skipSeed = builder.Configuration.GetValue<bool>("SkipDatabaseSeed");
    if (!skipSeed)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkitDbContext>();
        var adminSeedOptions = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AdminSeedOptions>>();
        try
        {
            await SeedData.EnsureSeededAsync(db, adminSeedOptions);
        }
        catch (Exception ex) when (IsDatabaseException(ex))
        {
            apiLogger.LogError(ex, "Skipping seed because the database is unavailable.");
        }
    }
}

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

static async Task<IResult> ExecuteDbAsync(
    Func<Task<IResult>> action,
    Microsoft.Extensions.Logging.ILogger logger,
    string operation)
{
    try
    {
        return await action();
    }
    catch (InvalidOperationException ex)
    {
        logger.LogWarning(ex, "Authorization context invalid while {Operation}.", operation);
        return Results.Unauthorized();
    }
    catch (Exception ex) when (IsDatabaseException(ex))
    {
        logger.LogError(ex, "Database unavailable while {Operation}.", operation);
        return Results.Problem(
            title: "Database unavailable",
            detail: $"The API could not access the database while {operation}. Check the database connection and try again.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}

static bool IsDatabaseException(Exception ex)
{
    Exception? current = ex;
    while (current is not null)
    {
        if (current is DbException or DbUpdateException or TimeoutException)
        {
            return true;
        }

        current = current.InnerException;
    }

    return false;
}

static string GenerateOwnerPassword()
{
    var raw = Guid.NewGuid().ToString("N");
    return char.ToUpper(raw[0]) + raw[1..10] + "!1";
}

static bool IsValidCompany(Company company) =>
    !string.IsNullOrWhiteSpace(company.Name) &&
    !string.IsNullOrWhiteSpace(company.Ssn) &&
    !string.IsNullOrWhiteSpace(company.Email) &&
    !string.IsNullOrWhiteSpace(company.Address) &&
    !string.IsNullOrWhiteSpace(company.Phone) &&
    !string.IsNullOrWhiteSpace(company.Owner);

static bool IsValidCustomer(Customer customer) =>
    !string.IsNullOrWhiteSpace(customer.Name) &&
    !string.IsNullOrWhiteSpace(customer.Ssn);

static bool IsValidEmployee(Employee employee) =>
    !string.IsNullOrWhiteSpace(employee.DisplayName) &&
    !string.IsNullOrWhiteSpace(employee.Ssn) &&
    !string.IsNullOrWhiteSpace(employee.Email);

static bool IsValidCredentials(string email, string password) =>
    !string.IsNullOrWhiteSpace(email) &&
    !string.IsNullOrWhiteSpace(password) &&
    password.Trim().Length >= 8;

static async Task LogStartupDatabaseStatusAsync(
    IServiceProvider services,
    Microsoft.Extensions.Logging.ILogger logger)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WorkitDbContext>();

    try
    {
        var canConnect = await db.Database.CanConnectAsync();
        if (canConnect)
        {
            logger.LogInformation("Database connection check succeeded at startup.");
            return;
        }

        logger.LogError("Database connection check failed at startup. The API will keep running and return 503 for database-backed endpoints.");
    }
    catch (Exception ex) when (IsDatabaseException(ex))
    {
        logger.LogError(ex, "Database connection check failed at startup. The API will keep running and return 503 for database-backed endpoints.");
    }
}

record SwitchCompanyRequest(Guid CompanyId);
record UpdateDrivingRateRequest(decimal UnitPrice);
record UpdateStandardHoursRequest(decimal StandardHoursPerDay);
record UpdatePaydayCredentialsRequest(string? ClientId, string? ClientSecret);
