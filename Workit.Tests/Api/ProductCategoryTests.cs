using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workit.Api.Data;
using Workit.Api.Endpoints;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>Categories as a first-class list: seeded from what is in use, resolved by name, renamed everywhere.</summary>
public class ProductCategoryTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static WorkitDbContext NewDb()
    {
        var db = new WorkitDbContext(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"cats-{Guid.NewGuid()}").Options);
        db.PaydayProducts.AddRange(
            new PaydayProductCache { CompanyId = CompanyId, PaydayId = Guid.NewGuid(), Sku = "A", Name = "Vír", Category = "Vír og strengir" },
            new PaydayProductCache { CompanyId = CompanyId, PaydayId = Guid.NewGuid(), Sku = "B", Name = "Rofi", Category = "Rofar" },
            new PaydayProductCache { CompanyId = CompanyId, PaydayId = Guid.NewGuid(), Sku = "C", Name = "Óflokkað", Category = "" },
            new PaydayProductCache { CompanyId = Guid.NewGuid(), PaydayId = Guid.NewGuid(), Sku = "X", Name = "Annað fyrirtæki", Category = "Leyndó" });
        db.Materials.Add(new Material { CompanyId = CompanyId, Name = "Kapall", Category = "Vír og strengir", IsActive = true });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Seed_CreatesACategoryForEveryNameInUse_OnceAndOnlyForTheCompany()
    {
        await using var db = NewDb();
        await ProductCategoryEndpoints.EnsureSeededAsync(db, CompanyId, CancellationToken.None);
        await ProductCategoryEndpoints.EnsureSeededAsync(db, CompanyId, CancellationToken.None);

        var rows = await ProductCategoryEndpoints.ListAsync(db, CompanyId, CancellationToken.None);
        rows.Select(r => r.Name).Should().Equal("Rofar", "Vír og strengir");
        rows.Single(r => r.Name == "Vír og strengir").Products.Should().Be(1);
        rows.Single(r => r.Name == "Vír og strengir").Materials.Should().Be(1);
    }

    [Fact]
    public async Task Resolve_MatchesCaseInsensitively_AndReturnsTheStoredSpelling()
    {
        await using var db = NewDb();
        await ProductCategoryEndpoints.EnsureSeededAsync(db, CompanyId, CancellationToken.None);

        (await ProductCategoryEndpoints.ResolveAsync(db, CompanyId, "  rofar ", CancellationToken.None)).Should().Be("Rofar");
        (await ProductCategoryEndpoints.ResolveAsync(db, CompanyId, "Lýsing", CancellationToken.None)).Should().BeNull();
        (await ProductCategoryEndpoints.ResolveAsync(db, CompanyId, "", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ResolveOrCreate_AddsAMissingCategory_ForAcceptedSuggestions()
    {
        await using var db = NewDb();
        var name = await ProductCategoryEndpoints.ResolveOrCreateAsync(db, CompanyId, "Lýsing", CancellationToken.None);
        name.Should().Be("Lýsing");
        (await db.ProductCategories.CountAsync(c => c.CompanyId == CompanyId && c.Name == "Lýsing")).Should().Be(1);
        (await ProductCategoryEndpoints.ResolveOrCreateAsync(db, CompanyId, "lýsing", CancellationToken.None)).Should().Be("Lýsing", "second call finds the first");
    }
}
