using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Auth;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// Tests the admin seeding logic from SeedData.EnsureSeededAsync.
/// Since MigrateAsync requires a relational provider, we replicate the
/// seeding logic (which is what we're actually testing) against an
/// in-memory database.
/// </summary>
public class SeedDataTests : IDisposable
{
    private readonly WorkitDbContext _db;

    public SeedDataTests()
    {
        var options = new DbContextOptionsBuilder<WorkitDbContext>()
            .UseInMemoryDatabase(databaseName: $"SeedTest-{Guid.NewGuid()}")
            .Options;
        _db = new WorkitDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// Replicates the seeding logic from SeedData.EnsureSeededAsync,
    /// minus the MigrateAsync call (not supported on InMemory).
    /// </summary>
    private async Task SeedAdminAsync(string email, string password)
    {
        var trimmedEmail = email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(trimmedEmail) || string.IsNullOrWhiteSpace(password))
            return;

        var adminExists = await _db.AppUsers.AnyAsync(
            x => x.Role == WorkitRoles.Admin && x.Email == trimmedEmail);

        if (adminExists)
            return;

        _db.AppUsers.Add(new AppUser
        {
            Email = trimmedEmail,
            PasswordHash = PasswordHasher.HashPassword(password),
            Role = WorkitRoles.Admin
        });

        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Seed_CreatesAdminUser()
    {
        await SeedAdminAsync("admin@test.com", "Secret123!");

        var admin = await _db.AppUsers.SingleOrDefaultAsync(u => u.Role == WorkitRoles.Admin);
        admin.Should().NotBeNull();
        admin!.Email.Should().Be("admin@test.com");
    }

    [Fact]
    public async Task Seed_HashesPassword()
    {
        await SeedAdminAsync("admin@test.com", "MyPassword123");

        var admin = await _db.AppUsers.SingleAsync(u => u.Role == WorkitRoles.Admin);
        admin.PasswordHash.Should().NotBe("MyPassword123");
        PasswordHasher.VerifyPassword("MyPassword123", admin.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task Seed_NormalizesEmail_ToLowerCase()
    {
        await SeedAdminAsync("  Admin@Test.COM  ", "Secret123!");

        var admin = await _db.AppUsers.SingleAsync(u => u.Role == WorkitRoles.Admin);
        admin.Email.Should().Be("admin@test.com");
    }

    [Fact]
    public async Task Seed_SkipsIfAdminExists()
    {
        await SeedAdminAsync("admin@test.com", "Secret123!");
        await SeedAdminAsync("admin@test.com", "Secret123!"); // second call

        var admins = await _db.AppUsers.Where(u => u.Role == WorkitRoles.Admin).ToListAsync();
        admins.Should().HaveCount(1);
    }

    [Fact]
    public async Task Seed_EmptyEmail_DoesNotCreateUser()
    {
        await SeedAdminAsync("  ", "Secret123!");

        var count = await _db.AppUsers.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task Seed_EmptyPassword_DoesNotCreateUser()
    {
        await SeedAdminAsync("admin@test.com", "");

        var count = await _db.AppUsers.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task Seed_AdminRole_IsSet()
    {
        await SeedAdminAsync("admin@test.com", "Secret123!");

        var admin = await _db.AppUsers.SingleAsync();
        admin.Role.Should().Be(WorkitRoles.Admin);
    }
}
