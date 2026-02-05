using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using System.Data.Common;
using Workit.Api.Data;
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

builder.Services.AddDbContext<WorkitDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("WorkitDb")
        ?? "Server=localhost;Database=WorkitDb;User Id=sa;Password=Your_password123;TrustServerCertificate=True";
    options.UseSqlServer(connectionString);
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

var api = app.MapGroup("/api");

api.MapGet("/companies", async (WorkitDbContext db, CancellationToken ct) =>
        await ExecuteDbAsync(
            async () => Results.Ok(await db.Companies.OrderBy(x => x.Name).ToListAsync(ct)),
            apiLogger,
            "loading companies"))
    .WithName("GetCompanies");

api.MapPost("/companies", async (WorkitDbContext db, Company company, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            db.Companies.Add(company);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/companies/{company.Id}", company);
        },
        apiLogger,
        "creating a company"))
    .WithName("CreateCompany");

api.MapGet("/customers", async (WorkitDbContext db, Guid companyId, CancellationToken ct) =>
        await ExecuteDbAsync(
            async () => Results.Ok(await db.Customers.Where(x => x.CompanyId == companyId).OrderBy(x => x.Name).ToListAsync(ct)),
            apiLogger,
            "loading customers"))
    .WithName("GetCustomers");

api.MapPost("/customers", async (WorkitDbContext db, Customer customer, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            db.Customers.Add(customer);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/customers/{customer.Id}", customer);
        },
        apiLogger,
        "creating a customer"))
    .WithName("CreateCustomer");

api.MapGet("/employees", async (WorkitDbContext db, Guid companyId, CancellationToken ct) =>
        await ExecuteDbAsync(
            async () => Results.Ok(await db.Employees.Where(x => x.CompanyId == companyId).OrderBy(x => x.DisplayName).ToListAsync(ct)),
            apiLogger,
            "loading employees"))
    .WithName("GetEmployees");

api.MapPost("/employees", async (WorkitDbContext db, Employee employee, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            db.Employees.Add(employee);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/employees/{employee.Id}", employee);
        },
        apiLogger,
        "creating an employee"))
    .WithName("CreateEmployee");

api.MapGet("/jobs", async (WorkitDbContext db, Guid companyId, CancellationToken ct) =>
        await ExecuteDbAsync(
            async () => Results.Ok(await db.Jobs.Where(x => x.CompanyId == companyId).OrderBy(x => x.Code).ToListAsync(ct)),
            apiLogger,
            "loading jobs"))
    .WithName("GetJobs");

api.MapPost("/jobs", async (WorkitDbContext db, Job job, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
            db.Jobs.Add(job);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/jobs/{job.Id}", job);
        },
        apiLogger,
        "creating a job"))
    .WithName("CreateJob");

api.MapGet("/timeentries", async (
        WorkitDbContext db,
        Guid companyId,
        Guid? employeeId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct) =>
    {
        var query = db.TimeEntries.Where(x => x.CompanyId == companyId);

        if (employeeId is not null)
        {
            query = query.Where(x => x.EmployeeId == employeeId);
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

api.MapPost("/timeentries", async (WorkitDbContext db, TimeEntry entry, CancellationToken ct) =>
        await ExecuteDbAsync(async () =>
        {
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
        try
        {
            await SeedData.EnsureSeededAsync(db);
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
