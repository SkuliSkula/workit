using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;

namespace Workit.Api.Services;

/// <summary>
/// Background service that automatically scans email inboxes for vendor invoices
/// every hour for companies that have AutoScanEnabled = true.
/// Disabled by default — the owner must turn it on in the Invoices settings page.
/// </summary>
public sealed class InvoiceScanBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<InvoiceScanBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Invoice background scanner started (interval: {Interval})", Interval);

        using var timer = new PeriodicTimer(Interval);

        // Run immediately on startup, then repeat on the timer
        await ScanAllAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ScanAllAsync(stoppingToken);
    }

    private async Task ScanAllAsync(CancellationToken ct)
    {
        using var scope  = scopeFactory.CreateScope();
        var db           = scope.ServiceProvider.GetRequiredService<WorkitDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<EmailScanService>();

        var enabledSettings = await db.EmailSettings
            .Where(s => s.AutoScanEnabled
                     && s.ImapHost != string.Empty
                     && s.Username  != string.Empty)
            .ToListAsync(ct);

        if (enabledSettings.Count == 0) return;

        logger.LogInformation("Auto-scanning {Count} mailbox(es)", enabledSettings.Count);

        foreach (var settings in enabledSettings)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var result = await emailService.ScanAsync(settings, settings.CompanyId);
                logger.LogInformation(
                    "Mailbox scan for company {CompanyId}: imported={Imported} skipped={Skipped} errors={Errors}",
                    settings.CompanyId, result.Imported, result.Skipped, result.Errors);
            }
            catch (Exception ex)
            {
                // Never let one company's failure stop the whole loop
                logger.LogError(ex, "Auto-scan failed for company {CompanyId}", settings.CompanyId);
            }
        }
    }
}
