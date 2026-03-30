using System.Text;
using Anthropic.SDK;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Api.Endpoints;
using Workit.Api.Services;
using Workit.Shared.Auth;

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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WorkitDbContext>();
    await db.Database.MigrateAsync();
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
app.MapCompanyEndpoints();
app.MapCustomerEndpoints();
app.MapEmployeeEndpoints();
app.MapJobEndpoints();
app.MapTimeEntryEndpoints();
app.MapToolEndpoints();
app.MapMaterialEndpoints();
app.MapEmailSettingsEndpoints();
app.MapInvoiceEndpoints();
app.MapAbsenceEndpoints();
app.MapWorkDutyEndpoints();
app.MapStatusEndpoints();
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

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
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
    catch (Exception ex) when (EndpointHelpers.IsDatabaseException(ex))
    {
        logger.LogError(ex, "Database connection check failed at startup. The API will keep running and return 503 for database-backed endpoints.");
    }
}
