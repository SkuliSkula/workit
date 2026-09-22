using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Workit.Api.Data;
using Workit.Api.Endpoints;
using Workit.Api.Services;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// The address helper: how a typed query becomes a Staðfangaskrá filter, how a
/// registry feature becomes a display line, and which contacts get suggested.
/// </summary>
public class AddressLookupTests
{
    // ── Query parsing ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Borgartún 26", "Borgartún", 26, null, null)]
    [InlineData("borgart 26a", "borgart", 26, "A", null)]
    [InlineData("Borgartún 26, 105", "Borgartún", 26, null, 105)]
    [InlineData("105 Borgartún 26", "Borgartún", 26, null, 105)]
    [InlineData("Laugavegur 105", "Laugavegur", 105, null, null)]        // 3 digits after the street = house, not postcode
    [InlineData("Hraun 1 Reyðarfjörður", "Hraun", 1, null, null)]         // trailing town word is dropped
    [InlineData("Suðurlandsbraut", "Suðurlandsbraut", null, null, null)]
    [InlineData("Efri Brú", "Efri Brú", null, null, null)]
    public void Parse_splits_street_house_letter_postcode(string q, string street, int? house, string? letter, int? post)
    {
        var p = AddressLookupService.Parse(q);
        p.Should().NotBeNull();
        p!.Street.Should().Be(street);
        p.HouseNumber.Should().Be(house);
        p.Letter.Should().Be(letter);
        p.PostCode.Should().Be(post);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("B")]
    [InlineData("26")]
    public void Parse_rejects_too_little(string q) => AddressLookupService.Parse(q).Should().BeNull();

    [Fact]
    public void Filter_with_house_number_narrows_to_the_house()
    {
        var f = AddressLookupService.BuildFilter(AddressLookupService.Parse("Borgartún 26a 105")!);
        f.Should().Be("HEITI_NF ILIKE 'Borgartún%' AND HUSNR = 26 AND BOKST = 'A' AND POSTNR = 105");
    }

    [Fact]
    public void Filter_without_house_number_also_matches_named_places()
    {
        var f = AddressLookupService.BuildFilter(AddressLookupService.Parse("Efri")!);
        f.Should().Be("(HEITI_NF ILIKE 'Efri%' OR SERHEITI ILIKE 'Efri%')");
    }

    [Fact]
    public void Filter_escapes_quotes_and_wildcards()
    {
        var f = AddressLookupService.BuildFilter(AddressLookupService.Parse("O'Brien%_")!);
        f.Should().Contain("'O''Brien%'").And.NotContain("%_");
    }

    // ── Feature → hit ────────────────────────────────────────────────────────

    private const string Feature = """
        {"type":"FeatureCollection","features":[
          {"type":"Feature","properties":{"HNITNUM":10001388,"LANDNR":102918,"POSTNR":105,"HEITI_NF":"Borgartún","HUSNR":26,"BOKST":null,"VIDSK":null,"SERHEITI":null,"N_HNIT_WGS84":64.1456416,"E_HNIT_WGS84":-21.8937886}},
          {"type":"Feature","properties":{"HNITNUM":2,"LANDNR":5,"POSTNR":801,"HEITI_NF":"Grímsnes","HUSNR":null,"BOKST":null,"VIDSK":null,"SERHEITI":"Efri-Brú","N_HNIT_WGS84":64.1,"E_HNIT_WGS84":-20.9}},
          {"type":"Feature","properties":{"HNITNUM":3,"LANDNR":6,"POSTNR":200,"HEITI_NF":"Hlíðasmári","HUSNR":8,"BOKST":"B","VIDSK":null,"SERHEITI":null,"N_HNIT_WGS84":64.1,"E_HNIT_WGS84":-21.8}},
          {"type":"Feature","properties":{"HNITNUM":4,"LANDNR":7,"POSTNR":105,"HEITI_NF":"Borgartún","HUSNR":8,"BOKST":null,"VIDSK":null,"SERHEITI":null,"N_HNIT_WGS84":null,"E_HNIT_WGS84":null}}
        ]}
        """;

    [Fact]
    public async Task Search_maps_features_to_display_lines_and_skips_unplaced_ones()
    {
        var service = NewService(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Feature, Encoding.UTF8, "application/json") });

        var result = await service.SearchAsync("Borgart", CancellationToken.None);

        result.LookupAvailable.Should().BeTrue();
        result.Hits.Select(h => h.Display).Should().Equal(
            "Borgartún 26, 105 Reykjavík",
            "Efri-Brú, Grímsnes, 801 Selfoss",
            "Hlíðasmári 8B, 200 Kópavogur");
        result.Hits[0].LandNr.Should().Be(102918);
        result.Hits[0].Latitude.Should().BeApproximately(64.14564, 0.0001);
    }

    [Fact]
    public async Task Search_reports_unavailable_when_the_registry_fails()
    {
        var service = NewService(_ => throw new HttpRequestException("down"));
        var result = await service.SearchAsync("Borgart", CancellationToken.None);
        result.LookupAvailable.Should().BeFalse();
        result.Hits.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_caches_per_normalised_query()
    {
        var calls = 0;
        var service = NewService(_ => { calls++; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Feature, Encoding.UTF8, "application/json") }; });
        await service.SearchAsync("Borgart 26", CancellationToken.None);
        await service.SearchAsync("borgart 26", CancellationToken.None);
        await service.SearchAsync("Borgart  26,", CancellationToken.None);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Search_sends_the_filter_to_the_registry()
    {
        string? requested = null;
        var service = NewService(req => { requested = req.RequestUri!.AbsoluteUri; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"features":[]}""", Encoding.UTF8, "application/json") }; });
        await service.SearchAsync("Borgartún 26", CancellationToken.None);
        requested.Should().StartWith("https://example.test/geoserver/wfs?")
            .And.Contain("typeNames=public:Stadfangaskra")
            .And.Contain(Uri.EscapeDataString("HEITI_NF ILIKE 'Borgartún%' AND HUSNR = 26"));
    }

    private static AddressLookupService NewService(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var factory = new StubHttpClientFactory(new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("https://example.test/geoserver/") });
        return new AddressLookupService(factory, new MemoryCache(new MemoryCacheOptions()), NullLogger<AddressLookupService>.Instance);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    // ── Postcodes ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(105, "Reykjavík")]
    [InlineData(200, "Kópavogur")]
    [InlineData(220, "Hafnarfjörður")]
    [InlineData(600, "Akureyri")]
    [InlineData(900, "Vestmannaeyjar")]
    [InlineData(999, null)]
    public void Postcodes_are_nominative(int code, string? town) => IcelandicPostcodes.TownOf(code).Should().Be(town);

    // ── Contact suggestions ──────────────────────────────────────────────────

    private static WorkitDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WorkitDbContext>().UseInMemoryDatabase($"contacts-{Guid.NewGuid()}").Options);

    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid OtherCompany = Guid.NewGuid();

    private static Job JobAt(string code, string location, int? landNr, string contact, string phone, int daysAgo, Guid? company = null) => new()
    {
        CompanyId = company ?? CompanyId, Name = code, Code = code, Location = location, AddressLandNr = landNr,
        ContactName = contact, ContactPhone = phone, CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
    };

    [Fact]
    public async Task Suggestions_come_from_same_parcel_newest_first_deduplicated_then_customer()
    {
        await using var db = NewDb();
        var customer = new Customer { CompanyId = CompanyId, Name = "Norðurljós Verk ehf.", ContactPerson = "Jón", Phone = "555 0000" };
        db.Customers.Add(customer);
        db.Jobs.AddRange(
            JobAt("MNT101", "Borgartún 26, 105 Reykjavík", 102918, "Guðrún húsvörður", "555 7788", 30),
            JobAt("MNT105", "Borgartún 26, 105 Reykjavík", 102918, "Guðrún Húsvörður", "5557788", 3),   // same person, different spelling → once
            JobAt("REP103", "Borgartún 26", 102918, "Páll", "555 1111", 10),
            JobAt("NI101", "Kópavogsbraut 1", 999, "Anna", "555 2222", 1),                              // other parcel
            JobAt("MNT200", "Borgartún 26, 105 Reykjavík", 102918, "Leaked", "1", 1, OtherCompany),        // other company
            JobAt("MNT300", "Borgartún 26, 105 Reykjavík", 102918, "", "", 1));                             // no contact
        await db.SaveChangesAsync();

        var s = await JobEndpoints.ContactSuggestionsAsync(db, CompanyId, 102918, null, customer.Id, null, CancellationToken.None);

        s.Select(x => x.Name).Should().Equal("Guðrún Húsvörður", "Páll", "Jón");
        s[0].Source.Should().StartWith("MNT105");
        s[2].Source.Should().Be("Customer");
        s[2].Phone.Should().Be("555 0000");
    }

    [Fact]
    public async Task Suggestions_fall_back_to_matching_location_text_and_skip_the_job_being_edited()
    {
        await using var db = NewDb();
        var editing = JobAt("MNT101", "Hraun 1, Reyðarfjörður", null, "Me", "1", 1);
        db.Jobs.AddRange(editing, JobAt("MNT102", "hraun 1, reyðarfjörður", null, "Sigga", "555 3333", 2));
        await db.SaveChangesAsync();

        var s = await JobEndpoints.ContactSuggestionsAsync(db, CompanyId, null, "Hraun 1, Reyðarfjörður", null, editing.Id, CancellationToken.None);

        s.Select(x => x.Name).Should().Equal("Sigga");
    }

    [Fact]
    public async Task Suggestions_are_empty_without_an_address_or_customer()
    {
        await using var db = NewDb();
        db.Jobs.Add(JobAt("MNT101", "Hraun 1", null, "Sigga", "1", 1));
        await db.SaveChangesAsync();
        (await JobEndpoints.ContactSuggestionsAsync(db, CompanyId, null, "  ", null, null, CancellationToken.None)).Should().BeEmpty();
    }
}
