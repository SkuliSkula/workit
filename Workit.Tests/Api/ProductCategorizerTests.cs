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
    [InlineData("Einþættur vír 1,5mm ² blár H07V-U 200m", "Vír og strengir")]
    [InlineData("TENGIDÓS AP10 HVÍT IP65 ABB, 104x104x48mm HALOGENFRÍ", "Dósir og kassar")]
    [InlineData("JUNG INNFELLDUR ÞRÝSTIROFI : 531 EU", "Rofar")]
    [InlineData("Super Retro GU10 550lm 830 DIM. 25.000klst CRI80 36°", "Lýsing")]
    [InlineData("DRAGBINDI JS+200X4,6 100STK HVÍT PA66", "Festingar og bindi")]
    [InlineData("EATON JARÐLEKAR. TYPE-A: PFGM-63/4/003-A", "Varbúnaður og töflur")]
    [InlineData("SHELLY PRO 2PM - 2RÁSA WIFI-RELAY MEÐ ORKUMÆLINGU", "Snjallbúnaður")]
    [InlineData("PRUFUTÆKI FYRIR RJ-45 PRÓFAR HVERT PAR, Grátt CAT5E CAT6", "Netbúnaður")]
    [InlineData("Rafhlöður AAA 8 stk í pk.", "Rafhlöður")]
    [InlineData("HETTUJAKKI DÖKKGR/SVARTUR M", "Vinnufatnaður")]
    [InlineData("Einangrunarband PVC Svart 20m", "Efni og lím")]
    [InlineData("KLÓ 32A 5P 6h P432-6S IP44 RAUÐ", "Tenglar og klær")]
    [InlineData("Vírklippur Heavy duty", "Verkfæri")]
    [InlineData("KAPALSPENNA C8-12W HVÍT 100/PAKK NAGLI 2,0x30mm TILLEX", "Festingar og bindi")]
    [InlineData("ÍDRÁTTARFEITI DYNABLUE D-35, 0,95L. POLYWATER", "Efni og lím")]
    [InlineData("Messingnippill MS-M12 IP68", "Rör og rennur")]
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
        var clear = ProductCategorizer.Suggest([P("Fínþættur vír 10mm ² Gulgrænn")], []).Single();
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
