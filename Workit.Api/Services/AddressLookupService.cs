using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Workit.Shared.Models;

namespace Workit.Api.Services;

public interface IAddressLookupService
{
    Task<AddressSearchResult> SearchAsync(string query, CancellationToken ct);
}

/// <summary>
/// Address typeahead over Staðfangaskrá, HMS's national address registry,
/// through its public WFS (GeoServer, layer public:Stadfangaskra, open data,
/// CC BY 4.0). No key, no account. The registry is meant for GIS clients, so
/// every distinct query is cached for a day and a failure is reported as
/// "lookup unavailable" rather than "no such address".
/// </summary>
/// <remarks>
/// Query parsing: "Borgart 26a 105" → street prefix "Borgart", house 26, letter
/// A, postcode 105. Farms and named places have no house number and live in
/// SERHEITI, so a bare word also matches those. Results are ordered street,
/// house number, letter — the same way a person would scan a list.
/// </remarks>
internal sealed partial class AddressLookupService(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    ILogger<AddressLookupService> logger) : IAddressLookupService
{
    public const string HttpClientName = "HmsStadfangaskra";
    private const int MaxHits = 20;
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);

    public async Task<AddressSearchResult> SearchAsync(string query, CancellationToken ct)
    {
        var parsed = Parse(query);
        if (parsed is null) return new AddressSearchResult([], true);

        var key = "addr:" + parsed.CacheKey;
        if (cache.TryGetValue(key, out IReadOnlyList<AddressHit>? cached) && cached is not null)
            return new AddressSearchResult(cached, true);

        try
        {
            var hits = await QueryAsync(parsed, ct);
            cache.Set(key, hits, CacheFor);
            return new AddressSearchResult(hits, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Staðfangaskrá lookup failed for {Query}", query);
            return new AddressSearchResult([], false);
        }
    }

    // ── Query parsing ────────────────────────────────────────────────────────

    internal sealed record ParsedQuery(string Street, int? HouseNumber, string? Letter, int? PostCode)
    {
        public string CacheKey => $"{Street.ToLowerInvariant()}|{HouseNumber}|{Letter}|{PostCode}";
    }

    [GeneratedRegex(@"^(?<num>\d{1,4})(?<letter>[A-Za-zÁÉÍÓÚÝÞÆÖáéíóúýþæö])?$")]
    private static partial Regex HouseToken();

    /// <summary>
    /// Street words, then an optional house number (+ letter), then an optional
    /// postcode. A 3-digit number straight after the street is a house number
    /// ("Laugavegur 105"); one after the house number, or leading, is a postcode.
    /// </summary>
    internal static ParsedQuery? Parse(string? query)
    {
        var tokens = (query ?? string.Empty)
            .Replace(',', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return null;

        var street = new List<string>();
        int? house = null; string? letter = null; int? post = null;
        foreach (var t in tokens)
        {
            var isPostcode = t.Length == 3 && int.TryParse(t, out var p) && p is >= 100 and <= 999;
            if (isPostcode && post is null && (street.Count == 0 || house is not null))
            {
                post = int.Parse(t, CultureInfo.InvariantCulture);
                continue;
            }
            var m = HouseToken().Match(t);
            if (m.Success && street.Count > 0 && house is null)
            {
                house = int.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture);
                letter = m.Groups["letter"].Success ? m.Groups["letter"].Value.ToUpperInvariant() : null;
                continue;
            }
            if (house is null) street.Add(t);
            // Words after the number ("Hraun 1 Reyðarfjörður") are the town; the
            // registry has no town column, so they are dropped rather than mis-filtered.
        }

        var streetText = string.Join(' ', street).Trim();
        if (streetText.Length < 2 || !streetText.Any(char.IsLetter)) return null;
        return new ParsedQuery(streetText, house, letter, post);
    }

    // ── WFS ──────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<AddressHit>> QueryAsync(ParsedQuery q, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var url = "wfs?service=WFS&version=2.0.0&request=GetFeature&typeNames=public:Stadfangaskra"
                  + "&outputFormat=application/json&srsName=EPSG:4326"
                  + $"&count={MaxHits}&sortBy=HEITI_NF,HUSNR,BOKST"
                  + "&CQL_FILTER=" + Uri.EscapeDataString(BuildFilter(q));

        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var hits = new List<AddressHit>();
        if (!doc.RootElement.TryGetProperty("features", out var features)) return hits;
        foreach (var f in features.EnumerateArray())
        {
            var hit = ToHit(f);
            if (hit is not null) hits.Add(hit);
        }
        // GeoServer's null ordering puts "1A" before "1"; a person expects 1, 1A, 2.
        return hits
            .OrderBy(h => h.Street, StringComparer.Create(new CultureInfo("is-IS"), ignoreCase: true))
            .ThenBy(h => Int(h.HouseNumber))
            .ThenBy(h => h.HouseNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.PostCode)
            .ToList();

        static int Int(string houseNumber)
        {
            var digits = new string(houseNumber.TakeWhile(char.IsDigit).ToArray());
            return digits.Length > 0 ? int.Parse(digits, CultureInfo.InvariantCulture) : int.MaxValue;
        }
    }

    /// <summary>ECQL. A bare word matches street names and named places (farms); a number narrows to the house.</summary>
    internal static string BuildFilter(ParsedQuery q)
    {
        var street = Escape(q.Street);
        var parts = new List<string>();
        if (q.HouseNumber is int n)
        {
            parts.Add($"HEITI_NF ILIKE '{street}%'");
            parts.Add($"HUSNR = {n}");
            if (q.Letter is not null) parts.Add($"BOKST = '{Escape(q.Letter)}'");
        }
        else
        {
            parts.Add($"(HEITI_NF ILIKE '{street}%' OR SERHEITI ILIKE '{street}%')");
        }
        if (q.PostCode is int p) parts.Add($"POSTNR = {p}");
        return string.Join(" AND ", parts);
    }

    private static string Escape(string s) => s.Replace("'", "''").Replace("%", "").Replace("_", "");

    private static AddressHit? ToHit(JsonElement feature)
    {
        if (!feature.TryGetProperty("properties", out var p)) return null;
        var lat = Num(p, "N_HNIT_WGS84");
        var lng = Num(p, "E_HNIT_WGS84");
        if (lat is null || lng is null) return null;

        var street = Str(p, "HEITI_NF");
        var husnr = Int(p, "HUSNR");
        var bokst = Str(p, "BOKST");
        var vidsk = Str(p, "VIDSK");           // "8-16" ranges
        var serheiti = Str(p, "SERHEITI");     // farm / named place
        var post = Int(p, "POSTNR");
        var town = IcelandicPostcodes.TownOf(post) ?? string.Empty;

        var houseNumber = husnr is int h ? h + bokst : string.Empty;
        if (!string.IsNullOrEmpty(vidsk) && vidsk != houseNumber) houseNumber = string.IsNullOrEmpty(houseNumber) ? vidsk : $"{houseNumber} ({vidsk})";

        var first = !string.IsNullOrEmpty(serheiti)
            ? (string.IsNullOrEmpty(houseNumber) ? $"{serheiti}, {street}" : $"{serheiti}, {street} {houseNumber}")
            : $"{street} {houseNumber}".Trim();
        var tail = string.Join(' ', new[] { post?.ToString(CultureInfo.InvariantCulture), town }.Where(x => !string.IsNullOrEmpty(x)));
        var display = string.IsNullOrEmpty(tail) ? first : $"{first}, {tail}";

        return new AddressHit(
            HnitNum: Long(p, "HNITNUM") ?? 0,
            LandNr: Int(p, "LANDNR"),
            Display: display,
            Street: street,
            HouseNumber: houseNumber,
            PostCode: post,
            Town: town,
            Latitude: lat.Value,
            Longitude: lng.Value);
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : string.Empty;
    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var i) ? i : null;
    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
