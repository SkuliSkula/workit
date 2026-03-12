using System.Data.Common;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Auth;
using Workit.Shared.Models;

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
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithOrigins(
                "https://localhost:7100",
                "https://localhost:7300",
                "https://localhost:7200",
                "http://localhost:5100",
                "http://localhost:5300",
                "http://localhost:5200",
                "http://192.168.86.26:5100",
                "http://192.168.86.26:5300",
                "http://192.168.86.31:5100",
                "http://192.168.86.31:5300");
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

            return Results.Ok(tokenFactory.CreateToken(user));
        },
        apiLogger,
        "authenticating a user"))
    .WithName("Login");

authApi.MapPost("/register-company", async (WorkitDbContext db, HttpContext httpContext, TokenFactory tokenFactory, RegisterCompanyRequest request, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!string.Equals(httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, WorkitRoles.Admin, StringComparison.Ordinal))
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
            await db.SaveChangesAsync(ct);

            return Results.Ok(tokenFactory.CreateToken(ownerUser));
        },
        apiLogger,
        "registering a company"))
    .RequireAuthorization()
    .WithName("RegisterCompany");

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

securedApi.MapGet("/customers", async (WorkitDbContext db, HttpContext httpContext, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            if (!httpContext.User.IsOwner())
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
            if (!httpContext.User.IsOwner())
            {
                return Results.Forbid();
            }

            if (!IsValidCustomer(customer))
            {
                return Results.BadRequest("Customer name, SSN, email, phone, and contact person are required.");
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
            if (!httpContext.User.IsOwner())
            {
                return Results.Forbid();
            }

            if (id != customer.Id)
            {
                return Results.BadRequest("Customer id mismatch.");
            }

            if (!IsValidCustomer(customer))
            {
                return Results.BadRequest("Customer name, SSN, email, phone, and contact person are required.");
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
            else if (!string.Equals(userContext.Role, WorkitRoles.Owner, StringComparison.Ordinal))
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
            if (!httpContext.User.IsOwner())
            {
                return Results.Forbid();
            }

            if (!IsValidEmployee(request.Employee))
            {
                return Results.BadRequest("Employee name, trade, SSN, email, phone, and contact person are required.");
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
            if (!httpContext.User.IsOwner())
            {
                return Results.Forbid();
            }

            if (id != employee.Id)
            {
                return Results.BadRequest("Employee id mismatch.");
            }

            if (!IsValidEmployee(employee))
            {
                return Results.BadRequest("Employee name, trade, SSN, email, phone, and contact person are required.");
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
            if (!httpContext.User.IsOwner())
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
            if (!httpContext.User.IsOwner())
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

            existing.CustomerId = job.CustomerId;
            existing.Code = job.Code.Trim();
            existing.Name = job.Name.Trim();

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
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct) =>
    {
        var userContext = httpContext.User.ToUserContext();
        var query = db.TimeEntries.Where(x => x.CompanyId == userContext.CompanyId);

        if (string.Equals(userContext.Role, WorkitRoles.Owner, StringComparison.Ordinal))
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
            if (!string.Equals(userContext.Role, WorkitRoles.Employee, StringComparison.Ordinal) ||
                userContext.EmployeeId is not Guid currentEmployeeId)
            {
                return Results.Forbid();
            }

            entry.CompanyId = userContext.CompanyId;
            entry.EmployeeId = currentEmployeeId;

            db.TimeEntries.Add(entry);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/timeentries/{entry.Id}", entry);
        },
        apiLogger,
        "creating a time entry"))
    .WithName("CreateTimeEntry");

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

if (app.Environment.IsDevelopment() && !isDesignTime)
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

static bool IsValidCompany(Company company) =>
    !string.IsNullOrWhiteSpace(company.Name) &&
    !string.IsNullOrWhiteSpace(company.Ssn) &&
    !string.IsNullOrWhiteSpace(company.Email) &&
    !string.IsNullOrWhiteSpace(company.Address) &&
    !string.IsNullOrWhiteSpace(company.Phone) &&
    !string.IsNullOrWhiteSpace(company.Owner);

static bool IsValidCustomer(Customer customer) =>
    !string.IsNullOrWhiteSpace(customer.Name) &&
    !string.IsNullOrWhiteSpace(customer.Ssn) &&
    !string.IsNullOrWhiteSpace(customer.Email) &&
    !string.IsNullOrWhiteSpace(customer.Phone) &&
    !string.IsNullOrWhiteSpace(customer.ContactPerson);

static bool IsValidEmployee(Employee employee) =>
    !string.IsNullOrWhiteSpace(employee.DisplayName) &&
    !string.IsNullOrWhiteSpace(employee.Trade) &&
    !string.IsNullOrWhiteSpace(employee.Ssn) &&
    !string.IsNullOrWhiteSpace(employee.Email) &&
    !string.IsNullOrWhiteSpace(employee.Phone) &&
    !string.IsNullOrWhiteSpace(employee.ContactPerson);

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
