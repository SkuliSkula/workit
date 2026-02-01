using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Shared.Models;

var builder = WebApplication.CreateBuilder(args);

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowBlazorApps");

app.MapGet("/api/companies", async (WorkitDbContext db, CancellationToken ct) =>
        await db.Companies.OrderBy(x => x.Name).ToListAsync(ct))
    .WithName("GetCompanies");

app.MapPost("/api/companies", async (WorkitDbContext db, Company company, CancellationToken ct) =>
    {
        db.Companies.Add(company);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/companies/{company.Id}", company);
    })
    .WithName("CreateCompany");

app.MapGet("/api/customers", async (WorkitDbContext db, Guid companyId, CancellationToken ct) =>
        await db.Customers.Where(x => x.CompanyId == companyId).OrderBy(x => x.Name).ToListAsync(ct))
    .WithName("GetCustomers");

app.MapPost("/api/customers", async (WorkitDbContext db, Customer customer, CancellationToken ct) =>
    {
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/customers/{customer.Id}", customer);
    })
    .WithName("CreateCustomer");

app.MapGet("/api/employees", async (WorkitDbContext db, Guid companyId, CancellationToken ct) =>
        await db.Employees.Where(x => x.CompanyId == companyId).OrderBy(x => x.DisplayName).ToListAsync(ct))
    .WithName("GetEmployees");

app.MapPost("/api/employees", async (WorkitDbContext db, Employee employee, CancellationToken ct) =>
    {
        db.Employees.Add(employee);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/employees/{employee.Id}", employee);
    })
    .WithName("CreateEmployee");

app.MapGet("/api/jobs", async (WorkitDbContext db, Guid companyId, CancellationToken ct) =>
        await db.Jobs.Where(x => x.CompanyId == companyId).OrderBy(x => x.Code).ToListAsync(ct))
    .WithName("GetJobs");

app.MapPost("/api/jobs", async (WorkitDbContext db, Job job, CancellationToken ct) =>
    {
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/jobs/{job.Id}", job);
    })
    .WithName("CreateJob");

app.MapGet("/api/timeentries", async (
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

        return await query.OrderByDescending(x => x.WorkDate).ToListAsync(ct);
    })
    .WithName("GetTimeEntries");

app.MapPost("/api/timeentries", async (WorkitDbContext db, TimeEntry entry, CancellationToken ct) =>
    {
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/timeentries/{entry.Id}", entry);
    })
    .WithName("CreateTimeEntry");

var isDesignTime = string.Equals(Environment.GetEnvironmentVariable("EF_DESIGN_TIME"), "true", StringComparison.OrdinalIgnoreCase);

if (app.Environment.IsDevelopment() && !isDesignTime)
{
    var skipSeed = builder.Configuration.GetValue<bool>("SkipDatabaseSeed");
    if (!skipSeed)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkitDbContext>();
        await SeedData.EnsureSeededAsync(db);
    }
}

app.Run();
