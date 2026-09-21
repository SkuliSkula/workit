using FluentAssertions;
using Workit.Api.Payday;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>The category suggester on real wholesaler names: seeds, learning from the owner, confidence, and "no idea".</summary>
public class ProductCategorizerTests
{
    private static PaydayProductCache P(string name, string category = "", string description = "") =>
        new() { Id = Guid.NewGuid(), Sku = name[..Math.Min(6, name.Length)], Name = name, Description = description, Category = category };

    [Theory]
    [InlineData("Einþættur vír 1,5mm ² blár H07V-U 200m", "Töfluvír")]
    [InlineData("TENGIDÓS AP10 HVÍT IP65 ABB, 104x104x48mm HALOGENFRÍ", "Lagnaefni")]
    [InlineData("JUNG INNFELLDUR ÞRÝSTIROFI : 531 EU", "Rofar og tenglar")]
    [InlineData("Super Retro GU10 550lm 830 DIM. 25.000klst CRI80 36°", "Perur og íhlutir")]
    [InlineData("DRAGBINDI JS+200X4,6 100STK HVÍT PA66", "Festingar")]
    [InlineData("EATON JARÐLEKAR. TYPE-A: PFGM-63/4/003-A", "Töflubúnaður")]
    [InlineData("SHELLY PRO 2PM - 2RÁSA WIFI-RELAY MEÐ ORKUMÆLINGU", "Snjalllausnir og hússtjórnarkerfi")]
    [InlineData("PRUFUTÆKI FYRIR RJ-45 PRÓFAR HVERT PAR, Grátt CAT5E CAT6", "Netefni")]
    [InlineData("Rafhlöður AAA 8 stk í pk.", "Rafhlöður almennar")]
    [InlineData("HETTUJAKKI DÖKKGR/SVARTUR M", "Vinnufatnaður")]
    [InlineData("Einangrunarband PVC Svart 20m", "Einangrunarefni")]
    [InlineData("KLÓ 32A 5P 6h P432-6S IP44 RAUÐ", "Klær, fjöltengi, framlengingar")]
    [InlineData("Vírklippur Heavy duty", "Verkfæri")]
    [InlineData("KAPALSPENNA C8-12W HVÍT 100/PAKK NAGLI 2,0x30mm TILLEX", "Festingar")]
    [InlineData("ÍDRÁTTARFEITI DYNABLUE D-35, 0,95L. POLYWATER", "Efni og lím")]
    [InlineData("Messingnippill MS-M12 IP68", "Lagnaleiðir")]
    public void Seeds_PlaceTypicalWholesalerNames(string name, string expected)
    {
        var s = ProductCategorizer.Suggest([P(name)], []).Single();
        s.Category.Should().Be(expected, string.Join(", ", s.Because));
        s.Because.Should().NotBeEmpty();
    }

    [Fact]
    public void Learning_FromTheOwnersCategories_WinsOverSeedNames()
    {
        // The owner files Shelly under "Hússtjórn" instead of the seed "Snjallbúnaður".
        var examples = new[]
        {
            P("SHELLY 1 MINI", "Hússtjórn"), P("SHELLY PLUS 2PM", "Hússtjórn"), P("SHELLY DIMMER 2", "Hússtjórn"),
            P("Plejd DIM-02", "Hússtjórn"), P("Plejd REL-01", "Hússtjórn"),
        };
        var s = ProductCategorizer.Suggest([P("SHELLY PRO 4PM RELAY")], examples).Single();
        s.Category.Should().Be("Hússtjórn");
    }

    [Fact]
    public void NoMatch_ComesBackWithoutACategory_SoTheOwnerFillsItIn()
    {
        var s = ProductCategorizer.Suggest([P("Vörunúmer 8817-QX")], []).Single();
        s.Category.Should().BeNull();
        s.Confidence.Should().Be(ProductCategorizer.Confidence.Low);
    }

    [Fact]
    public void Confidence_IsHighWhenOneCategoryDominates_LowWhenSplit()
    {
        var clear = ProductCategorizer.Suggest([P("DRAGBINDI JS+200X4,6 100STK HVÍT PA66")], []).Single();
        clear.Confidence.Should().Be(ProductCategorizer.Confidence.High);

        // "rofi" and "ljós" pull equally hard in different directions.
        var split = ProductCategorizer.Suggest([P("Rofi ljós")], []).Single();
        split.Confidence.Should().NotBe(ProductCategorizer.Confidence.High);
    }

    [Fact]
    public void Tokens_FoldIcelandic_DropSizesAndCounts()
    {
        ProductCategorizer.Tokens("TENGIDÓS AP10 HVÍT IP65, 104x104x48mm 100stk/pk þrýstirofi")
            .Should().Contain(["tengidos", "hvit", "ip65", "thrystirofi"])
            .And.NotContain(["104x104x48mm", "100stk", "pk"]);
    }
}
