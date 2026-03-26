using Workit.Api.Data;
using static Workit.Api.Endpoints.EndpointHelpers;

namespace Workit.Api.Endpoints;

internal static class StatusEndpoints
{
    internal static void MapStatusEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").WithTags("Status");
        var logger = app.Logger;

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
                logger,
                "checking database availability"))
            .WithName("GetDatabaseStatus");
    }
}
