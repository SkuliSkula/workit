namespace Workit.Api.Services;

/// <summary>
/// Postcode → town, nominative ("200 Kópavogur"). Staðfangaskrá carries the
/// postcode but not the town; this list is Pósturinn's postnumer.txt with the
/// dative place names put back in the nominative, as addresses are written.
/// </summary>
internal static class IcelandicPostcodes
{
    public static string? TownOf(int? postcode) =>
        postcode is int p && Towns.TryGetValue(p, out var town) ? town : null;

    private static readonly Dictionary<int, string> Towns = new()
    {
        [101] = "Reykjavík", [102] = "Reykjavík", [103] = "Reykjavík", [104] = "Reykjavík",
        [105] = "Reykjavík", [107] = "Reykjavík", [108] = "Reykjavík", [109] = "Reykjavík",
        [110] = "Reykjavík", [111] = "Reykjavík", [112] = "Reykjavík", [113] = "Reykjavík",
        [116] = "Reykjavík", [121] = "Reykjavík", [123] = "Reykjavík", [124] = "Reykjavík",
        [125] = "Reykjavík", [127] = "Reykjavík", [128] = "Reykjavík", [129] = "Reykjavík",
        [130] = "Reykjavík", [132] = "Reykjavík", [150] = "Reykjavík", [155] = "Reykjavík",
        [161] = "Reykjavík", [162] = "Reykjavík", [170] = "Seltjarnarnes", [172] = "Seltjarnarnes",
        [190] = "Vogar", [191] = "Vogar", [200] = "Kópavogur", [201] = "Kópavogur",
        [202] = "Kópavogur", [203] = "Kópavogur", [206] = "Kópavogur", [210] = "Garðabær",
        [212] = "Garðabær", [220] = "Hafnarfjörður", [221] = "Hafnarfjörður", [222] = "Hafnarfjörður",
        [225] = "Garðabær", [230] = "Reykjanesbær", [232] = "Reykjanesbær", [233] = "Reykjanesbær",
        [235] = "Keflavíkurflugvöllur", [240] = "Grindavík", [241] = "Grindavík", [245] = "Sandgerði",
        [246] = "Sandgerði", [250] = "Garður", [251] = "Garður", [260] = "Reykjanesbær",
        [262] = "Reykjanesbær", [270] = "Mosfellsbær", [271] = "Mosfellsbær", [276] = "Kjós",
        [300] = "Akranes", [301] = "Akranes", [302] = "Akranes", [310] = "Borgarnes",
        [311] = "Borgarnes", [320] = "Reykholt", [340] = "Stykkishólmur", [341] = "Stykkishólmur",
        [342] = "Stykkishólmur", [345] = "Flatey", [350] = "Grundarfjörður", [351] = "Grundarfjörður",
        [355] = "Ólafsvík", [356] = "Snæfellsbær", [360] = "Hellissandur", [370] = "Búðardalur",
        [371] = "Búðardalur", [380] = "Reykhólahreppur", [381] = "Reykhólahreppur", [400] = "Ísafjörður",
        [401] = "Ísafjörður", [410] = "Hnífsdalur", [415] = "Bolungarvík", [416] = "Bolungarvík",
        [420] = "Súðavík", [421] = "Súðavík", [425] = "Flateyri", [426] = "Flateyri",
        [430] = "Suðureyri", [431] = "Suðureyri", [450] = "Patreksfjörður", [451] = "Patreksfjörður",
        [460] = "Tálknafjörður", [461] = "Tálknafjörður", [465] = "Bíldudalur", [466] = "Bíldudalur",
        [470] = "Þingeyri", [471] = "Þingeyri", [500] = "Staður", [510] = "Hólmavík",
        [511] = "Hólmavík", [512] = "Hólmavík", [520] = "Drangsnes", [524] = "Árneshreppur",
        [530] = "Hvammstangi", [531] = "Hvammstangi", [540] = "Blönduós", [541] = "Blönduós",
        [545] = "Skagaströnd", [546] = "Skagaströnd", [550] = "Sauðárkrókur", [551] = "Sauðárkrókur",
        [560] = "Varmahlíð", [561] = "Varmahlíð", [565] = "Hofsós", [566] = "Hofsós",
        [570] = "Fljót", [580] = "Siglufjörður", [581] = "Siglufjörður", [600] = "Akureyri",
        [601] = "Akureyri", [602] = "Akureyri", [603] = "Akureyri", [604] = "Akureyri",
        [605] = "Akureyri", [606] = "Akureyri", [607] = "Akureyri", [610] = "Grenivík",
        [611] = "Grímsey", [616] = "Grenivík", [620] = "Dalvík", [621] = "Dalvík",
        [625] = "Ólafsfjörður", [626] = "Ólafsfjörður", [630] = "Hrísey", [640] = "Húsavík",
        [641] = "Húsavík", [645] = "Fosshóll", [650] = "Laugar", [660] = "Mývatn",
        [670] = "Kópasker", [671] = "Kópasker", [675] = "Raufarhöfn", [676] = "Raufarhöfn",
        [680] = "Þórshöfn", [681] = "Þórshöfn", [685] = "Bakkafjörður", [686] = "Bakkafjörður",
        [690] = "Vopnafjörður", [691] = "Vopnafjörður", [700] = "Egilsstaðir", [701] = "Egilsstaðir",
        [710] = "Seyðisfjörður", [711] = "Seyðisfjörður", [715] = "Mjóifjörður", [720] = "Borgarfjörður eystri",
        [721] = "Borgarfjörður eystri", [730] = "Reyðarfjörður", [731] = "Reyðarfjörður", [735] = "Eskifjörður",
        [736] = "Eskifjörður", [740] = "Neskaupstaður", [741] = "Neskaupstaður", [750] = "Fáskrúðsfjörður",
        [751] = "Fáskrúðsfjörður", [755] = "Stöðvarfjörður", [756] = "Stöðvarfjörður", [760] = "Breiðdalsvík",
        [761] = "Breiðdalsvík", [765] = "Djúpivogur", [766] = "Djúpivogur", [780] = "Höfn í Hornafirði",
        [781] = "Höfn í Hornafirði", [785] = "Öræfi", [800] = "Selfoss", [801] = "Selfoss",
        [802] = "Selfoss", [803] = "Selfoss", [804] = "Selfoss", [805] = "Selfoss",
        [806] = "Selfoss", [810] = "Hveragerði", [815] = "Þorlákshöfn", [816] = "Ölfus",
        [820] = "Eyrarbakki", [825] = "Stokkseyri", [840] = "Laugarvatn", [845] = "Flúðir",
        [846] = "Flúðir", [850] = "Hella", [851] = "Hella", [860] = "Hvolsvöllur",
        [861] = "Hvolsvöllur", [870] = "Vík", [871] = "Vík", [880] = "Kirkjubæjarklaustur",
        [881] = "Kirkjubæjarklaustur", [900] = "Vestmannaeyjar", [902] = "Vestmannaeyjar",
    };
}
