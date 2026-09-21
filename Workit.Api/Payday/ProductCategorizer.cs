using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Workit.Shared.Models;

namespace Workit.Api.Payday;

/// <summary>
/// Suggests a category for a product from its name and description. Two
/// sources, summed per category: seed rules for the vocabulary of an Icelandic
/// electrical wholesaler (word stems → category), and what the owner has
/// already categorised in this company (each token of a categorised product
/// votes for its category). Corrections therefore make the next run better,
/// and a company's own category names win over the seed names once in use.
/// Deterministic, offline, explainable — every suggestion says which words
/// decided it.
/// </summary>
public static partial class ProductCategorizer
{
    /// <summary>Seed rules: normalised stem (prefix match on a token) → category, weight.</summary>
    private static readonly (string Stem, string Category, double Weight)[] Seeds =
    [
        // Cable and wire
        ("vir", "Vír og strengir", 3), ("streng", "Vír og strengir", 3), ("snura", "Vír og strengir", 3), ("kapal", "Vír og strengir", 2),
        ("kabal", "Vír og strengir", 2), ("h07v", "Vír og strengir", 3), ("h05", "Vír og strengir", 2), ("h03", "Vír og strengir", 2),
        ("n1xe", "Vír og strengir", 3), ("ekk", "Vír og strengir", 2), ("nyy", "Vír og strengir", 2), ("aflstreng", "Vír og strengir", 3),
        ("jardvir", "Vír og strengir", 3), ("fjolthaett", "Vír og strengir", 2), ("einthaett", "Vír og strengir", 2), ("finthaett", "Vír og strengir", 2),
        ("mm2", "Vír og strengir", 1), ("mm²", "Vír og strengir", 1), ("tengivir", "Vír og strengir", 3),
        // Switches
        ("rofi", "Rofar", 3), ("rofa", "Rofar", 3), ("vippa", "Rofar", 3), ("dimm", "Rofar", 3), ("thrystirofi", "Rofar", 3), ("kronurofi", "Rofar", 3),
        ("hreyfiskynjari", "Rofar", 2), ("skynjari", "Rofar", 1), ("rofalidi", "Snjallbúnaður", 2),
        // Sockets and plugs
        ("tengill", "Tenglar og klær", 3), ("tenglar", "Tenglar og klær", 3), ("tengla", "Tenglar og klær", 3), ("klo", "Tenglar og klær", 3),
        ("innstunga", "Tenglar og klær", 3), ("fjoltengi", "Tenglar og klær", 3), ("framlengingar", "Tenglar og klær", 2), ("cee", "Tenglar og klær", 1),
        ("plexo", "Tenglar og klær", 2),
        // Boxes
        ("dos", "Dósir og kassar", 3), ("tengidos", "Dósir og kassar", 3), ("kassi", "Dósir og kassar", 3), ("kassar", "Dósir og kassar", 3),
        ("greinidos", "Dósir og kassar", 3), ("lok", "Dósir og kassar", 1), ("endaplata", "Rammar og hlífar", 2),
        // Frames and covers for switch ranges
        ("rammi", "Rammar og hlífar", 3), ("ramma", "Rammar og hlífar", 3), ("hlif", "Rammar og hlífar", 2), ("botn", "Rammar og hlífar", 1),
        // Lighting
        ("ljos", "Lýsing", 3), ("led", "Lýsing", 2), ("lampi", "Lýsing", 3), ("lampa", "Lýsing", 3), ("lumen", "Lýsing", 2), ("lm", "Lýsing", 1),
        ("gu10", "Lýsing", 3), ("e27", "Lýsing", 3), ("e14", "Lýsing", 3), ("pera", "Lýsing", 3), ("perur", "Lýsing", 3), ("kastari", "Lýsing", 3),
        ("neydarljos", "Lýsing", 3), ("loftljos", "Lýsing", 3), ("spotti", "Lýsing", 2), ("spot", "Lýsing", 1), ("armatur", "Lýsing", 2),
        ("dimmanleg", "Lýsing", 1), ("cri", "Lýsing", 1), ("kelvin", "Lýsing", 1),
        // Fixings, ties, clips
        ("klemma", "Festingar og bindi", 3), ("klemmur", "Festingar og bindi", 3), ("festing", "Festingar og bindi", 3), ("festi", "Festingar og bindi", 2),
        ("spenna", "Festingar og bindi", 2), ("dragbindi", "Festingar og bindi", 3), ("bindi", "Festingar og bindi", 2), ("nagli", "Festingar og bindi", 2),
        ("skrufa", "Festingar og bindi", 2), ("skrufur", "Festingar og bindi", 2), ("bolti", "Festingar og bindi", 2), ("ro", "Festingar og bindi", 1),
        ("veggfesti", "Festingar og bindi", 3), ("kapalgrind", "Festingar og bindi", 2), ("upphengi", "Festingar og bindi", 2),
        // Conduit and trunking
        ("ror", "Rör og rennur", 3), ("rora", "Rör og rennur", 3), ("renna", "Rör og rennur", 3), ("rennur", "Rör og rennur", 3), ("kapalrenna", "Rör og rennur", 3),
        ("barki", "Rör og rennur", 3), ("idrattar", "Rör og rennur", 2), ("bogi", "Rör og rennur", 1), ("muffa", "Rör og rennur", 2), ("nippill", "Rör og rennur", 2),
        // Protection and distribution
        ("oryggi", "Varbúnaður og töflur", 3), ("lekalidi", "Varbúnaður og töflur", 3), ("lekar", "Varbúnaður og töflur", 2), ("sjalfvar", "Varbúnaður og töflur", 3),
        ("var", "Varbúnaður og töflur", 1), ("varrofi", "Varbúnaður og töflur", 3), ("tafla", "Varbúnaður og töflur", 3), ("toflu", "Varbúnaður og töflur", 3),
        ("pfgm", "Varbúnaður og töflur", 2), ("eaton", "Varbúnaður og töflur", 1), ("hager", "Varbúnaður og töflur", 1), ("skinna", "Varbúnaður og töflur", 2),
        ("teinn", "Varbúnaður og töflur", 2), ("spennugjafi", "Varbúnaður og töflur", 2), ("spennir", "Varbúnaður og töflur", 2), ("lidi", "Varbúnaður og töflur", 1),
        ("relay", "Varbúnaður og töflur", 1), ("kontaktor", "Varbúnaður og töflur", 2),
        // Smart home / automation
        ("shelly", "Snjallbúnaður", 3), ("plejd", "Snjallbúnaður", 3), ("wifi", "Snjallbúnaður", 2), ("bluetooth", "Snjallbúnaður", 2),
        ("zigbee", "Snjallbúnaður", 3), ("smart", "Snjallbúnaður", 2), ("knx", "Snjallbúnaður", 3), ("gateway", "Snjallbúnaður", 2),
        // Network
        ("rj", "Netbúnaður", 2), ("rj45", "Netbúnaður", 3), ("cat5", "Netbúnaður", 3), ("cat6", "Netbúnaður", 3), ("cat", "Netbúnaður", 1),
        ("netkapall", "Netbúnaður", 3), ("patch", "Netbúnaður", 3), ("rack", "Netbúnaður", 2), ("skipuleggjari", "Netbúnaður", 2), ("excel", "Netbúnaður", 1),
        ("keystone", "Netbúnaður", 3), ("ljosleidari", "Netbúnaður", 3), ("switch", "Netbúnaður", 1), ("utp", "Netbúnaður", 2), ("ftp", "Netbúnaður", 1),
        // Tools
        ("bor", "Verkfæri", 2), ("borasett", "Verkfæri", 3), ("klippur", "Verkfæri", 3), ("skrufjarn", "Verkfæri", 3), ("verkfaer", "Verkfæri", 3),
        ("tosk", "Verkfæri", 2), ("hamar", "Verkfæri", 3), ("tong", "Verkfæri", 3), ("prufutaeki", "Verkfæri", 3), ("maelir", "Verkfæri", 3),
        ("dewalt", "Verkfæri", 2), ("makita", "Verkfæri", 2), ("bosch", "Verkfæri", 1), ("knipex", "Verkfæri", 2), ("hnifur", "Verkfæri", 3),
        ("sog", "Verkfæri", 2), ("blad", "Verkfæri", 1), ("bitar", "Verkfæri", 2), ("vasaljos", "Verkfæri", 2), ("hofudljos", "Verkfæri", 2),
        // Batteries
        ("rafhlod", "Rafhlöður", 3), ("rafhlad", "Rafhlöður", 3), ("battery", "Rafhlöður", 3), ("aaa", "Rafhlöður", 2), ("aa", "Rafhlöður", 1), ("9v", "Rafhlöður", 1),
        // Workwear
        ("jakki", "Vinnufatnaður", 3), ("bolur", "Vinnufatnaður", 3), ("buxur", "Vinnufatnaður", 3), ("hettu", "Vinnufatnaður", 2), ("peysa", "Vinnufatnaður", 3),
        ("hanskar", "Vinnufatnaður", 3), ("skor", "Vinnufatnaður", 2), ("vesti", "Vinnufatnaður", 3), ("hufa", "Vinnufatnaður", 3), ("vinnufat", "Vinnufatnaður", 3),
        ("hjalm", "Vinnufatnaður", 3), ("gleraugu", "Vinnufatnaður", 3),
        // Consumables
        ("kitti", "Efni og lím", 3), ("lim", "Efni og lím", 3), ("band", "Efni og lím", 1), ("einangrunarband", "Efni og lím", 3), ("tape", "Efni og lím", 3),
        ("feiti", "Efni og lím", 3), ("sprey", "Efni og lím", 3), ("spray", "Efni og lím", 3), ("silikon", "Efni og lím", 3), ("eldvarnar", "Efni og lím", 2),
        ("merki", "Efni og lím", 2), ("merking", "Efni og lím", 2),
        // Terminals
        ("tengi", "Tengi og klemmur", 2), ("wago", "Tengi og klemmur", 3), ("tengiklemma", "Tengi og klemmur", 3), ("skoklemma", "Tengi og klemmur", 3),
        ("endahulsa", "Tengi og klemmur", 3), ("kapalsko", "Tengi og klemmur", 3), ("raðtengi", "Tengi og klemmur", 3), ("radtengi", "Tengi og klemmur", 3),
        // Operating supplies
        ("ruslapok", "Rekstrarvörur", 3), ("pappir", "Rekstrarvörur", 2), ("hreinsi", "Rekstrarvörur", 3), ("klutur", "Rekstrarvörur", 3),
        // Heating / ventilation
        ("hitari", "Hitun og loftræsting", 3), ("ofn", "Hitun og loftræsting", 2), ("vifta", "Hitun og loftræsting", 3), ("blasari", "Hitun og loftræsting", 2),
        ("gólfhiti", "Hitun og loftræsting", 3), ("golfhiti", "Hitun og loftræsting", 3), ("thermostat", "Hitun og loftræsting", 3), ("hitastillir", "Hitun og loftræsting", 3),
    ];

    /// <summary>How sure a suggestion is; the dialog pre-ticks High and Medium.</summary>
    public enum Confidence { Low, Medium, High }

    public sealed record Suggestion(Guid ProductId, string Sku, string Name, string? Category, Confidence Confidence, IReadOnlyList<string> Because);

    /// <summary>
    /// Suggestions for <paramref name="targets"/>, learning from <paramref name="examples"/>
    /// (the company's already-categorised products). Products with no match come back
    /// with a null category so the owner can fill them in by hand.
    /// </summary>
    public static List<Suggestion> Suggest(IEnumerable<PaydayProductCache> targets, IEnumerable<PaydayProductCache> examples)
    {
        // token → category → weight, learned. Log-scaled so one hugely repeated word
        // ("hvítt") cannot outvote a decisive one ("tengidós").
        var learned = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
        foreach (var e in examples)
        {
            if (string.IsNullOrWhiteSpace(e.Category)) continue;
            foreach (var t in Tokens(e.Name + " " + e.Description).Distinct())
            {
                if (!learned.TryGetValue(t, out var byCat)) learned[t] = byCat = new(StringComparer.Ordinal);
                byCat[e.Category] = byCat.GetValueOrDefault(e.Category) + 1;
            }
        }

        var result = new List<Suggestion>();
        foreach (var p in targets)
        {
            var scores  = new Dictionary<string, double>(StringComparer.Ordinal);
            var because = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var tokens  = Tokens(p.Name + " " + p.Description).Distinct().ToList();

            foreach (var token in tokens)
            {
                // A word the owner has already filed somewhere follows the owner, not the seed list.
                var hasLearned = learned.TryGetValue(token, out var byCat);
                if (!hasLearned)
                {
                    foreach (var (stem, category, weight) in Seeds)
                    {
                        var factor = MatchFactor(token, stem);
                        if (factor == 0) continue;
                        scores[category] = scores.GetValueOrDefault(category) + weight * factor;
                        Add(because, category, token);
                    }
                }
                if (hasLearned)
                {
                    foreach (var (category, n) in byCat!)
                    {
                        // A token seen with this category 1× adds 1.5, 10× adds ~3.9 — the owner's own data
                        // outweighs a seed word once a category has a few examples.
                        scores[category] = scores.GetValueOrDefault(category) + 1.5 + Math.Log(n);
                        Add(because, category, token);
                    }
                }
            }

            if (scores.Count == 0)
            {
                result.Add(new Suggestion(p.Id, p.Sku, p.Name, null, Confidence.Low, []));
                continue;
            }

            var ranked = scores.OrderByDescending(kv => kv.Value).ToList();
            var (best, top) = (ranked[0].Key, ranked[0].Value);
            var runnerUp = ranked.Count > 1 ? ranked[1].Value : 0;
            var share = top / scores.Values.Sum();
            var confidence = top >= 3 && share >= 0.6 && top >= runnerUp * 2 ? Confidence.High
                           : top >= 2 && share >= 0.45 ? Confidence.Medium
                           : Confidence.Low;
            result.Add(new Suggestion(p.Id, p.Sku, p.Name, best, confidence, because[best].Distinct().Take(4).ToList()));
        }
        return result;
    }

    private static void Add(Dictionary<string, List<string>> because, string category, string token)
    {
        if (!because.TryGetValue(category, out var list)) because[category] = list = [];
        list.Add(token);
    }

    /// <summary>
    /// How well a stem fits a token, as a weight factor. Icelandic compounds put the
    /// head last — "vírklippur" are klippur (tools), "kapalspenna" is a spenna (clip),
    /// "ídráttarfeiti" is feiti (grease) — so a stem that ends the token counts
    /// most, a stem that only starts a longer compound counts least, and a one- or
    /// two-letter stem ("ro", "aa") must be the whole token.
    /// </summary>
    private static double MatchFactor(string token, string stem)
    {
        if (stem.Length <= 2) return token == stem ? 1 : 0;
        if (token == stem) return 1;
        if (token.EndsWith(stem, StringComparison.Ordinal)) return 1.4;
        if (token.StartsWith(stem, StringComparison.Ordinal)) return token.Length - stem.Length <= 3 ? 1 : 0.5;
        // Inside a longer compound ("messingnippill" → nippill needs ≥ 5 letters to avoid noise).
        if (stem.Length >= 5 && token.Contains(stem, StringComparison.Ordinal)) return 0.9;
        return 0;
    }

    [GeneratedRegex(@"[^a-z0-9²]+")]
    private static partial Regex NonWord();

    /// <summary>
    /// Lower-case ASCII-folded words (þ→th, ð→d, æ→ae, ö→o, accents dropped), split on
    /// anything else; pure numbers and units ("100stk", "19mm", "2x") are dropped so a
    /// size never decides a category.
    /// </summary>
    internal static IEnumerable<string> Tokens(string text)
    {
        var folded = Fold(text).ToLowerInvariant();
        foreach (var raw in NonWord().Split(folded))
        {
            if (raw.Length < 2) continue;
            if (raw.All(char.IsDigit)) continue;
            if (IsMeasurement(raw) || IsUnit(raw) || Dimension().IsMatch(raw)) continue;
            yield return raw;
        }
    }

    private static bool IsUnit(string t) =>
        t is "stk" or "pk" or "pakk" or "mm" or "cm" or "ml" or "kw" or "ma" or "ah" or "my" or "klst" or "st";

    /// <summary>"104x104x48mm", "2x30mm", "200x4" — sizes, never a category.</summary>
    [GeneratedRegex(@"^\d+(,\d+)?(x\d+(,\d+)?)+[a-z]*$")]
    private static partial Regex Dimension();

    private static bool IsMeasurement(string t)
    {
        var i = 0;
        while (i < t.Length && (char.IsDigit(t[i]) || t[i] == ',' || t[i] == '.')) i++;
        if (i == 0) return false;
        var unit = t[i..];
        return unit is "stk" or "pk" or "mm" or "cm" or "m" or "ml" or "l" or "w" or "kw" or "v" or "a" or "ma" or "ah" or "x" or "p" or "f" or "u" or "my" or "klst" or "lm" or "k" or "mm2" or "mm²" or "st" or "pakk";
    }

    private static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
        {
            switch (ch)
            {
                case 'þ': case 'Þ': sb.Append("th"); break;
                case 'ð': case 'Ð': sb.Append('d'); break;
                case 'æ': case 'Æ': sb.Append("ae"); break;
                case 'ø': case 'Ø': sb.Append('o'); break;
                case '²': sb.Append("mm2"); break; // "1,5mm ²" → the unit token, dropped by IsMeasurement or kept as mm2
                default:
                    if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }
}
