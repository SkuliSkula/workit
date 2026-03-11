using Microsoft.EntityFrameworkCore;

namespace Workit.Api.Data;

public static class SeedData
{
    public static async Task EnsureSeededAsync(WorkitDbContext dbContext, CancellationToken cancellationToken = default)
        => await dbContext.Database.MigrateAsync(cancellationToken);
}
