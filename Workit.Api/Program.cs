using System.Text;
using Anthropic.SDK;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PostHog;
using Serilog;
using Serilog.Events;
using Workit.Api.Analytics;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Endpoints;
using Workit.Api.Services;
using Workit.Shared.Auth;
using Workit.Shared.Payday;

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
                    // Static site that hosts the password-reset page the
                    // forgot-password email links to.
                    "https://help.workit.is",
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

// Data Protection — encrypts credentials stored in the database.
// Keys are persisted to a directory so they survive restarts.
// In production set DataProtection:KeyPath in appsettings or an env-var.
var keyPath = builder.Configuration["DataProtection:KeyPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
    .SetApplicationName("Workit");
builder.Services.AddSingleton<ICredentialProtectionService, CredentialProtectionService>();

builder.Services.AddDbContext<WorkitDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("WorkitDb")
        ?? "Host=localhost;Port=5432;Database=workkit;Username=postgres;Password=postgres";
    options.UseNpgsql(connectionString);
});

// ── PDF invoice parsing (PdfPig + Claude) ────────────────────────────────────
// Kept for reading supplier product codes and purchase prices off a Payday
// expense attachment; the IMAP inbox that used to feed it was dropped (2026-09-22).
var anthropicApiKey = builder.Configuration["Anthropic:ApiKey"] ?? string.Empty;
builder.Services.AddSingleton(_ => new AnthropicClient(new Anthropic.SDK.APIAuthentication(anthropicApiKey)));
builder.Services.AddScoped<InvoiceParserService>();

// ── Analytics (PostHog) ────────────────────────────────────────────────────────
var postHogApiKey = builder.Configuration["PostHog:ProjectApiKey"];
if (!string.IsNullOrWhiteSpace(postHogApiKey))
{
    var postHogHost = builder.Configuration["PostHog:HostUrl"] ?? "https://eu.i.posthog.com/";
    builder.Services.AddSingleton<IPostHogClient>(
        _ => new PostHogClient(new PostHogOptions
        {
            ProjectApiKey = postHogApiKey,
            HostUrl        = new Uri(postHogHost),
        }));
    builder.Services.AddSingleton<IAnalyticsService, PostHogAnalyticsService>();
}
else
{
    builder.Services.AddSingleton<IAnalyticsService, NullAnalyticsService>();
}

// ── File storage (job attachments) ───────────────────────────────────────────────
// R2 (Cloudflare object storage) in production; a local-disk fallback in dev so the
// feature works without cloud credentials. The API is the only process with access.
var storageOptions = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
builder.Services.AddSingleton(storageOptions);
if (storageOptions.IsR2Configured)
{
    builder.Services.AddSingleton<IFileStorageService, R2FileStorageService>();
    Log.Information(
        "Job attachments: storing in Cloudflare R2 — bucket {Bucket} at {Endpoint}.",
        storageOptions.BucketName, storageOptions.ServiceUrl);
}
else
{
    builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();
    var missing = storageOptions.MissingR2Settings;
    if (missing.Count == 4)
    {
        Log.Information(
            "Job attachments: storing on local disk at {Path}. R2 is not configured — "
            + "fine for development, but uploads do not survive a container rebuild.",
            storageOptions.LocalPath);
    }
    else
    {
        // A partially-filled config is almost always a typo, and silently using
        // local disk in production would lose files on the next deploy.
        Log.Warning(
            "Job attachments: R2 is only partially configured, so falling back to local disk at {Path}. "
            + "Missing setting(s): {MissingSettings}. Uploads will NOT go to R2 and do not survive a container rebuild.",
            storageOptions.LocalPath, string.Join(", ", missing));
    }
}

// ── Payday ─────────────────────────────────────────────────────────────────────
// The API is the only process that talks to Payday. It registers the "PaydayApi"
// HttpClient plus the direct Payday clients; /api/payday/* proxies them for the
// Owner app using each company's encrypted credentials (see PaydayEndpoints).
// Payday:BaseUrl switches every Payday call (token included) to the sandbox for local runs.
builder.Services.AddPaydayApiClients(builder.Configuration["Payday:BaseUrl"]);
builder.Services.AddScoped<Workit.Api.Payday.PaydayProductSyncService>();
builder.Services.AddScoped<Workit.Api.Payday.MaterialsMigrationService>();
builder.Services.AddScoped<Workit.Api.Payday.PaydayCustomerSyncService>();
builder.Services.AddHostedService<Workit.Api.Payday.PaydaySyncBackgroundService>();

// ── Address lookup (HMS Staðfangaskrá, open data) ─────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient(Workit.Api.Services.AddressLookupService.HttpClientName, client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AddressLookup:WfsBaseUrl"] ?? "https://gis.fasteignaskra.is/geoserver/");
    client.Timeout = TimeSpan.FromSeconds(6);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Workit/1.0 (+https://workit.is)");
});
builder.Services.AddSingleton<Workit.Api.Services.IAddressLookupService, Workit.Api.Services.AddressLookupService>();

// ── Email (Resend) ─────────────────────────────────────────────────────────────
var resendApiKey = builder.Configuration["Resend:ApiKey"];
if (!string.IsNullOrWhiteSpace(resendApiKey))
{
    builder.Services.AddHttpClient("Resend", client =>
    {
        client.BaseAddress = new Uri("https://api.resend.com/");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", resendApiKey);
    });
    builder.Services.AddScoped<IEmailService, ResendEmailService>();
}
else
{
    builder.Services.AddSingleton<IEmailService, NullEmailService>();
}

builder.Services.AddScoped<IAccountInviteService, AccountInviteService>();

var app = builder.Build();
Microsoft.Extensions.Logging.ILogger apiLogger = app.Logger;

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WorkitDbContext>();
    await db.Database.MigrateAsync();

    // Ensure every Owner with a company has an Employee record
    var ownersWithoutEmployee = await db.AppUsers
        .Where(u => u.Role == WorkitRoles.Owner
                  && u.CompanyId.HasValue && u.CompanyId.Value != Guid.Empty
                  && u.EmployeeId == null)
        .ToListAsync();

    foreach (var owner in ownersWithoutEmployee)
    {
        // Check if an Employee already exists in this company with the same email
        var existing = await db.Employees
            .FirstOrDefaultAsync(e => e.CompanyId == owner.CompanyId!.Value && e.Email == owner.Email);

        if (existing is not null)
        {
            owner.EmployeeId = existing.Id;
        }
        else
        {
            var emp = OwnerEmployeeHelper.CreateEmployeeForOwner(owner, owner.CompanyId!.Value);
            db.Employees.Add(emp);
        }
    }

    if (ownersWithoutEmployee.Count > 0)
    {
        await db.SaveChangesAsync();
        apiLogger.LogInformation("Created Employee records for {Count} owner(s).", ownersWithoutEmployee.Count);
    }
}

// ── One-off demo seeding ───────────────────────────────────────────────────────
// `dotnet Workit.Api.dll --seed-demo` builds the App Store review company and
// exits without serving. A command-line flag rather than an endpoint: it must
// not be reachable over HTTP in production, and it only ever touches the demo
// company's own rows.
//
// This must run here — immediately after migrations and before the startup
// tasks — not later in the file. Once hosted services are in play, a
// background service can throw, and the default
// BackgroundServiceExceptionBehavior.StopHost then tears down the host and
// disposes the service provider out from under this block.
if (args.Contains("--seed-demo"))
{
    using var demoScope = app.Services.CreateScope();
    var demoDb = demoScope.ServiceProvider.GetRequiredService<WorkitDbContext>();
    var summary = await DemoDataSeeder.SeedAsync(demoDb);
    Console.WriteLine(summary);
    return;
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowBlazorApps");
app.UseAuthentication();
app.UseAuthorization();

// ── Map endpoints ──────────────────────────────────────────────────────────────
app.MapAuthEndpoints();
app.MapAdminOverviewEndpoints();
app.MapCompanyEndpoints();
app.MapCustomerEndpoints();
app.MapEmployeeEndpoints();
app.MapJobEndpoints();
app.MapJobTaskEndpoints();
app.MapTimeEntryEndpoints();
app.MapToolEndpoints();
app.MapMaterialEndpoints();
app.MapAbsenceEndpoints();
app.MapWorkDutyEndpoints();
app.MapStatusEndpoints();
app.MapSalesInvoiceEndpoints();
app.MapExpenseEndpoints();
app.MapExpenseAutoLinkEndpoints();
app.MapJobAttachmentEndpoints();
app.MapPaydayEndpoints();
app.MapPaydayProductEndpoints();
app.MapPaydayCustomerEndpoints();
app.MapProductCategoryEndpoints();
app.MapPayrollEndpoints();
app.MapDevSeedEndpoints();

// ── Startup tasks ──────────────────────────────────────────────────────────────
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
        catch (Exception ex) when (EndpointHelpers.IsDatabaseException(ex))
        {
            apiLogger.LogError(ex, "Skipping seed because the database is unavailable.");
        }
    }
}

// Flush any queued PostHog events on shutdown. This has to run while the host
// is still alive: app.Run() disposes the service provider before returning, so
// resolving services after it throws ObjectDisposedException.
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopped.Register(() =>
{
    var analytics = app.Services.GetService<IAnalyticsService>();
    if (analytics is not PostHogAnalyticsService)
        return;

    var postHog = app.Services.GetService<IPostHogClient>();
    if (postHog is null)
        return;

    try
    {
        postHog.FlushAsync().GetAwaiter().GetResult();
    }
    catch (Exception ex)
    {
        apiLogger.LogWarning(ex, "Failed to flush PostHog events during shutdown.");
    }
});

app.Run();

Log.CloseAndFlush();

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
    catch (Exception ex) when (EndpointHelpers.IsDatabaseException(ex))
    {
        logger.LogError(ex, "Database connection check failed at startup. The API will keep running and return 503 for database-backed endpoints.");
    }
}
