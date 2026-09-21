using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Api.Services;
using Workit.Shared.Payday;

namespace Workit.Api.Payday;

/// <summary>
/// Keeps every connected company in step with Payday without anyone pressing
/// Sync now: the product cache (and, where switched on, materials), and
/// customers in both directions — pending Workit edits go out, Payday's list
/// comes in. One company's failure never stops the loop.
/// </summary>
public sealed class PaydaySyncBackgroundService(IServiceScopeFactory scopeFactory, ILogger<PaydaySyncBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Payday sync started (interval: {Interval})", Interval);
        using var timer = new PeriodicTimer(Interval);
        // First pass a minute after startup so a deploy does not hammer Payday alongside migrations.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch (OperationCanceledException) { return; }
        await SyncAllAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await SyncAllAsync(stoppingToken);
    }

    private async Task SyncAllAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db         = scope.ServiceProvider.GetRequiredService<WorkitDbContext>();
        var protection = scope.ServiceProvider.GetRequiredService<ICredentialProtectionService>();

        var companies = await db.Companies.AsNoTracking()
            .Where(c => c.PaydayClientId != null && c.PaydayClientSecret != null)
            .Select(c => new { c.Id, c.Name, c.PaydayClientId, c.PaydayClientSecret })
            .ToListAsync(ct);
        if (companies.Count == 0) return;

        foreach (var company in companies)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                // A scope per company: the token service is scoped and holds one company's credentials.
                using var companyScope = scopeFactory.CreateScope();
                var tokens = companyScope.ServiceProvider.GetRequiredService<IPaydayTokenService>();
                var clientId = protection.Unprotect(company.PaydayClientId);
                var secret   = protection.Unprotect(company.PaydayClientSecret);
                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret)) continue;
                tokens.SetCredentials(clientId, secret);

                var products = companyScope.ServiceProvider.GetRequiredService<PaydayProductSyncService>();
                var outcome = await products.SyncAsync(company.Id, ct);
                if (outcome.Error is not null)
                    logger.LogWarning("Payday product sync for {Company} failed: {Error}", company.Name, outcome.Error);
                else
                    logger.LogInformation("Payday product sync for {Company}: {Fetched} products, {Added} new, {Archived} archived",
                        company.Name, outcome.Result!.Fetched, outcome.Result.Added, outcome.Result.Archived);

                var customers = companyScope.ServiceProvider.GetRequiredService<PaydayCustomerSyncService>();
                var customerOutcome = await customers.SyncAsync(company.Id, ct);
                if (customerOutcome.Error is not null)
                    logger.LogWarning("Payday customer sync for {Company} failed: {Error}", company.Name, customerOutcome.Error);
                else
                    logger.LogInformation("Payday customer sync for {Company}: {Fetched} customers, {Added} new, {Linked} linked, {Pushed} pushed, {PushFailed} push failures",
                        company.Name, customerOutcome.Result!.Fetched, customerOutcome.Result.Added, customerOutcome.Result.Linked, customerOutcome.Result.Pushed, customerOutcome.Result.PushFailed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Payday product sync failed for company {CompanyId}", company.Id);
            }
        }
    }
}
