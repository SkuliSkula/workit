using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Workit.Api.Auth;
using Workit.Shared.Auth;
using Workit.Shared.Models;

namespace Workit.Api.Data;

public static class SeedData
{
    public static async Task EnsureSeededAsync(
        WorkitDbContext dbContext,
        IOptions<AdminSeedOptions> adminSeedOptions,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        var options = adminSeedOptions.Value;
        var email = options.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(options.Password))
        {
            return;
        }

        var adminExists = await dbContext.AppUsers.AnyAsync(
            x => x.Role == WorkitRoles.Admin && x.Email == email,
            cancellationToken);

        if (adminExists)
        {
            return;
        }

        dbContext.AppUsers.Add(new AppUser
        {
            Email = email,
            PasswordHash = PasswordHasher.HashPassword(options.Password),
            Role = WorkitRoles.Admin
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
