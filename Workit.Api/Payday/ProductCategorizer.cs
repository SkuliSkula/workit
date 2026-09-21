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
    /// <summary>
    /// The supplier's taxonomy (Johan Rönning's Vörulisti, second level, brand rows
    /// collapsed) plus a few Workit-only groups the supplier has no shelf for.
    /// Offered to every company as its starting category list.
    /// </summary>
    public static readonly IReadOnlyList<(string Group, string Name)> StandardCategories =
    [
        ("Rafstrengir", "Aflstrengir"), ("Rafstrengir", "Gúmmístrengir"), ("Rafstrengir", "Merkjastrengir"), ("Rafstrengir", "Plaststrengir og snúrur"),
        ("Rafstrengir", "Skipastrengir"), ("Rafstrengir", "Stýristrengir"), ("Rafstrengir", "Töfluvír"), ("Rafstrengir", "Ídráttarvír"),
        ("Lagnaefni", "Lagnaefni"), ("Lagnaefni", "Lagnaleiðir"), ("Lagnaefni", "Festingar"), ("Lagnaefni", "Rofar og tenglar"),
        ("Lagnaefni", "Klær, fjöltengi, framlengingar"), ("Lagnaefni", "Iðnaðartenglar"), ("Lagnaefni", "Hreyfi- og viðveruskynjarar"),
        ("Lagnaefni", "Ljósdeyfar í dósir"), ("Lagnaefni", "Dyrasímar"), ("Lagnaefni", "Verkfæri"),
        ("Töflubúnaður", "Töfluskápar"), ("Töflubúnaður", "Töflubúnaður"), ("Töflubúnaður", "Afl- og skilrofar"), ("Töflubúnaður", "Greinatöflur"),
        ("Töflubúnaður", "Tengibox"), ("Töflubúnaður", "Tengikubbar"), ("Töflubúnaður", "Snjalllausnir og hússtjórnarkerfi"),
        ("Stýribúnaður", "Rofabúnaður"), ("Stýribúnaður", "Snarar og rofar"), ("Stýribúnaður", "Gaumljós og þrýstihnappar"), ("Stýribúnaður", "Nemar, vakar og liðar"),
        ("Stýribúnaður", "Mótorar, hraðast. og mjúkræsar"), ("Stýribúnaður", "Mælar og mælitæki"), ("Stýribúnaður", "Aflgjafar"), ("Stýribúnaður", "Varaaflgjafar"),
        ("Stýribúnaður", "Rafhlöður almennar"), ("Stýribúnaður", "Hleðsla rafbíla"),
        ("Tengibúnaður", "Tengiefni"), ("Tengibúnaður", "Einangrunarefni"), ("Tengibúnaður", "Tengimerkingar"), ("Tengibúnaður", "Veitubúnaður"),
        ("Ljósbúnaður", "Almenn lýsing"), ("Ljósbúnaður", "Almenn útilýsing"), ("Ljósbúnaður", "Iðnaðar-, götu- og flóðlýsing"), ("Ljósbúnaður", "Neyðarlýsing"),
        ("Ljósbúnaður", "LED-borðar og fylgihlutir"), ("Ljósbúnaður", "Perur og íhlutir"), ("Ljósbúnaður", "Snjallljós og perur"), ("Ljósbúnaður", "Kastarar í brautir"),
        ("Ljósbúnaður", "Kastarabrautir"), ("Ljósbúnaður", "Lampabrautir"), ("Ljósbúnaður", "Aukahlutir lýsingar"),
        ("Hitabúnaður", "Hitastrengir"), ("Hitabúnaður", "Hitamottur"), ("Hitabúnaður", "Ofnar og hitakútar"), ("Hitabúnaður", "Hitablásarar"),
        ("Hitabúnaður", "Hitastillar"), ("Hitabúnaður", "Viftur og kæling"), ("Hitabúnaður", "Gluggastýringar"),
        ("Fjarskiptabúnaður", "Netstrengir"), ("Fjarskiptabúnaður", "Netefni"), ("Fjarskiptabúnaður", "Ljósleiðarastrengir"), ("Fjarskiptabúnaður", "Ljósleiðaratengingar"),
        ("Fjarskiptabúnaður", "Netskiptar"), ("Fjarskiptabúnaður", "Þráðlaus netbúnaður"), ("Fjarskiptabúnaður", "19\" Tölvuskápar"),
        ("Fjarskiptabúnaður", "Fjarskiptamælar"), ("Fjarskiptabúnaður", "Brunakerfi"),
        // Workit's own — the supplier has no shelf for these.
        ("Annað", "Efni og lím"), ("Annað", "Vinnufatnaður"), ("Annað", "Rekstrarvörur"),
    ];

    /// <summary>Seed rules: normalised stem → category (a <see cref="StandardCategories"/> name), weight.</summary>
    private static readonly (string Stem, string Category, double Weight)[] Seeds =
    [
        // Rafstrengir
        ("aflstreng", "Aflstrengir", 3), ("n1xe", "Aflstrengir", 3), ("nyy", "Aflstrengir", 3), ("jardvir", "Aflstrengir", 2), ("jardstreng", "Aflstrengir", 3),
        ("ekk", "Plaststrengir og snúrur", 3), ("plaststreng", "Plaststrengir og snúrur", 3), ("snura", "Plaststrengir og snúrur", 3), ("h05", "Plaststrengir og snúrur", 3),
        ("h03", "Plaststrengir og snúrur", 3), ("pvc", "Plaststrengir og snúrur", 0.5),
        ("toflu", "Töfluvír", 1), ("vir", "Töfluvír", 2), ("h07v", "Töfluvír", 3), ("einthaett", "Töfluvír", 2), ("fjolthaett", "Töfluvír", 2), ("finthaett", "Töfluvír", 2),
        ("mm2", "Töfluvír", 0.5), ("tengivir", "Töfluvír", 3),
        ("styristreng", "Stýristrengir", 3), ("olflex", "Stýristrengir", 3), ("yslcy", "Stýristrengir", 3), ("merkjastreng", "Merkjastrengir", 3), ("gummistreng", "Gúmmístrengir", 3),
        ("h07rn", "Gúmmístrengir", 3), ("skipastreng", "Skipastrengir", 3), ("idrattarvir", "Ídráttarvír", 3), ("streng", "Plaststrengir og snúrur", 1), ("kapal", "Plaststrengir og snúrur", 1),
        ("kabal", "Plaststrengir og snúrur", 1),
        // Lagnaefni
        ("rofi", "Rofar og tenglar", 3), ("rofa", "Rofar og tenglar", 2), ("vippa", "Rofar og tenglar", 3), ("thrystirofi", "Rofar og tenglar", 3), ("kronurofi", "Rofar og tenglar", 3),
        ("tengill", "Rofar og tenglar", 3), ("tenglar", "Rofar og tenglar", 3), ("tengla", "Rofar og tenglar", 3), ("innstunga", "Rofar og tenglar", 3), ("rammi", "Rofar og tenglar", 3),
        ("ramma", "Rofar og tenglar", 3), ("hlif", "Rofar og tenglar", 1), ("endaplata", "Rofar og tenglar", 2), ("plexo", "Rofar og tenglar", 2), ("berker", "Rofar og tenglar", 2),
        ("gira", "Rofar og tenglar", 2), ("elko", "Rofar og tenglar", 2), ("bticino", "Rofar og tenglar", 2), ("jung", "Rofar og tenglar", 2), ("merten", "Rofar og tenglar", 2),
        ("dimm", "Ljósdeyfar í dósir", 3), ("ljosdeyf", "Ljósdeyfar í dósir", 3),
        ("hreyfiskynjari", "Hreyfi- og viðveruskynjarar", 3), ("vidveruskynjari", "Hreyfi- og viðveruskynjarar", 3), ("skynjari", "Hreyfi- og viðveruskynjarar", 2),
        ("klo", "Klær, fjöltengi, framlengingar", 3), ("fjoltengi", "Klær, fjöltengi, framlengingar", 3), ("framlengingar", "Klær, fjöltengi, framlengingar", 3), ("framlenging", "Klær, fjöltengi, framlengingar", 3),
        ("cee", "Iðnaðartenglar", 3), ("idnadartengill", "Iðnaðartenglar", 3), ("idnadarkl", "Iðnaðartenglar", 3),
        ("dos", "Lagnaefni", 3), ("tengidos", "Lagnaefni", 3), ("greinidos", "Lagnaefni", 3), ("lok", "Lagnaefni", 1), ("dosalok", "Lagnaefni", 3),
        ("ror", "Lagnaleiðir", 3), ("rora", "Lagnaleiðir", 3), ("renna", "Lagnaleiðir", 3), ("rennur", "Lagnaleiðir", 3), ("kapalrenna", "Lagnaleiðir", 3), ("barki", "Lagnaleiðir", 3),
        ("idrattar", "Lagnaleiðir", 2), ("bogi", "Lagnaleiðir", 1), ("muffa", "Lagnaleiðir", 2), ("nippill", "Lagnaleiðir", 2), ("kapalgrind", "Lagnaleiðir", 2), ("kapalstigi", "Lagnaleiðir", 3),
        ("klemma", "Festingar", 3), ("klemmur", "Festingar", 3), ("festing", "Festingar", 3), ("festi", "Festingar", 2), ("spenna", "Festingar", 2), ("dragbindi", "Festingar", 3),
        ("bindi", "Festingar", 2), ("nagli", "Festingar", 2), ("skrufa", "Festingar", 2), ("skrufur", "Festingar", 2), ("bolti", "Festingar", 2), ("ro", "Festingar", 1),
        ("veggfesti", "Festingar", 3), ("upphengi", "Festingar", 2), ("tappi", "Festingar", 2),
        ("dyrasimi", "Dyrasímar", 3), ("dyrasim", "Dyrasímar", 3), ("dyrabjalla", "Dyrasímar", 3),
        ("bor", "Verkfæri", 2), ("borasett", "Verkfæri", 3), ("klippur", "Verkfæri", 3), ("skrufjarn", "Verkfæri", 3), ("verkfaer", "Verkfæri", 3), ("tosk", "Verkfæri", 2),
        ("hamar", "Verkfæri", 3), ("tong", "Verkfæri", 3), ("prufutaeki", "Verkfæri", 3), ("dewalt", "Verkfæri", 2), ("makita", "Verkfæri", 2), ("bosch", "Verkfæri", 1),
        ("knipex", "Verkfæri", 2), ("hnifur", "Verkfæri", 3), ("sog", "Verkfæri", 2), ("blad", "Verkfæri", 1), ("bitar", "Verkfæri", 2), ("vasaljos", "Verkfæri", 2), ("hofudljos", "Verkfæri", 2),
        // Töflubúnaður
        ("tafla", "Töfluskápar", 2), ("skapur", "Töfluskápar", 3), ("skap", "Töfluskápar", 2), ("veggskapur", "Töfluskápar", 3), ("golfskapur", "Töfluskápar", 3),
        ("oryggi", "Töflubúnaður", 3), ("lekalidi", "Töflubúnaður", 3), ("lekar", "Töflubúnaður", 2), ("sjalfvar", "Töflubúnaður", 3), ("var", "Töflubúnaður", 1),
        ("varrofi", "Töflubúnaður", 3), ("pfgm", "Töflubúnaður", 2), ("skinna", "Töflubúnaður", 2), ("teinn", "Töflubúnaður", 2), ("eaton", "Töflubúnaður", 1), ("hager", "Töflubúnaður", 1),
        ("aflrofi", "Afl- og skilrofar", 3), ("skilrofi", "Afl- og skilrofar", 3), ("greinatafla", "Greinatöflur", 3), ("greinatoflur", "Greinatöflur", 3),
        ("tengibox", "Tengibox", 3), ("kassi", "Tengibox", 2), ("kassar", "Tengibox", 2),
        ("tengikubb", "Tengikubbar", 3), ("wago", "Tengikubbar", 3),
        ("shelly", "Snjalllausnir og hússtjórnarkerfi", 3), ("plejd", "Snjalllausnir og hússtjórnarkerfi", 3), ("knx", "Snjalllausnir og hússtjórnarkerfi", 3), ("zigbee", "Snjalllausnir og hússtjórnarkerfi", 3),
        ("smart", "Snjalllausnir og hússtjórnarkerfi", 2), ("gateway", "Snjalllausnir og hússtjórnarkerfi", 2), ("bluetooth", "Snjalllausnir og hússtjórnarkerfi", 2), ("husstjorn", "Snjalllausnir og hússtjórnarkerfi", 3),
        ("rofalidi", "Snjalllausnir og hússtjórnarkerfi", 2),
        // Stýribúnaður
        ("gaumljos", "Gaumljós og þrýstihnappar", 3), ("thrystihnapp", "Gaumljós og þrýstihnappar", 3), ("hnappur", "Gaumljós og þrýstihnappar", 2),
        ("nemi", "Nemar, vakar og liðar", 3), ("vaki", "Nemar, vakar og liðar", 3), ("lidi", "Nemar, vakar og liðar", 2), ("relay", "Nemar, vakar og liðar", 2), ("kontaktor", "Nemar, vakar og liðar", 3),
        ("timalidi", "Nemar, vakar og liðar", 3), ("spennulidi", "Nemar, vakar og liðar", 3),
        ("motor", "Mótorar, hraðast. og mjúkræsar", 3), ("hradastyring", "Mótorar, hraðast. og mjúkræsar", 3), ("mjukraesir", "Mótorar, hraðast. og mjúkræsar", 3),
        ("maelir", "Mælar og mælitæki", 3), ("maelitaeki", "Mælar og mælitæki", 3), ("orkumaelir", "Mælar og mælitæki", 3),
        ("aflgjafi", "Aflgjafar", 3), ("spennugjafi", "Aflgjafar", 3), ("spennir", "Aflgjafar", 2), ("ups", "Varaaflgjafar", 3), ("varaaflgjafi", "Varaaflgjafar", 3),
        ("rafhlod", "Rafhlöður almennar", 3), ("rafhlad", "Rafhlöður almennar", 3), ("battery", "Rafhlöður almennar", 3), ("aaa", "Rafhlöður almennar", 2), ("aa", "Rafhlöður almennar", 1), ("9v", "Rafhlöður almennar", 1),
        ("hledslustod", "Hleðsla rafbíla", 3), ("hledslusnura", "Hleðsla rafbíla", 3), ("rafbil", "Hleðsla rafbíla", 3), ("hledslu", "Hleðsla rafbíla", 2), ("wallbox", "Hleðsla rafbíla", 3), ("t2", "Hleðsla rafbíla", 1),
        ("rofabunadur", "Rofabúnaður", 3), ("snari", "Snarar og rofar", 3), ("snarar", "Snarar og rofar", 3), ("kambrofi", "Snarar og rofar", 3),
        // Tengibúnaður
        ("tengi", "Tengiefni", 2), ("tengiklemma", "Tengiefni", 3), ("skoklemma", "Tengiefni", 3), ("endahulsa", "Tengiefni", 3), ("kapalsko", "Tengiefni", 3), ("radtengi", "Tengiefni", 3),
        ("raðtengi", "Tengiefni", 3), ("tengistykki", "Tengiefni", 3),
        ("einangrunarband", "Einangrunarefni", 3), ("tape", "Einangrunarefni", 3), ("band", "Einangrunarefni", 1), ("herpihulsa", "Einangrunarefni", 3), ("herpi", "Einangrunarefni", 2),
        ("einangrun", "Einangrunarefni", 2),
        ("merki", "Tengimerkingar", 2), ("merking", "Tengimerkingar", 3), ("merkimidi", "Tengimerkingar", 3), ("merkihulsa", "Tengimerkingar", 3),
        ("heimtaug", "Veitubúnaður", 3), ("veitu", "Veitubúnaður", 3), ("stofn", "Veitubúnaður", 2), ("maelaskapur", "Veitubúnaður", 3),
        // Ljósbúnaður
        ("ljos", "Almenn lýsing", 2), ("lampi", "Almenn lýsing", 3), ("lampa", "Almenn lýsing", 3), ("loftljos", "Almenn lýsing", 3), ("armatur", "Almenn lýsing", 2), ("panel", "Almenn lýsing", 2),
        ("led", "Almenn lýsing", 1), ("lumen", "Almenn lýsing", 1), ("lm", "Almenn lýsing", 0.5), ("cri", "Almenn lýsing", 1), ("kelvin", "Almenn lýsing", 1), ("dimmanleg", "Almenn lýsing", 1),
        ("utiljos", "Almenn útilýsing", 3), ("uti", "Almenn útilýsing", 1), ("veggljos", "Almenn útilýsing", 2), ("gardljos", "Almenn útilýsing", 3),
        ("flodljos", "Iðnaðar-, götu- og flóðlýsing", 3), ("gotuljos", "Iðnaðar-, götu- og flóðlýsing", 3), ("idnadarljos", "Iðnaðar-, götu- og flóðlýsing", 3), ("highbay", "Iðnaðar-, götu- og flóðlýsing", 3),
        ("neydarljos", "Neyðarlýsing", 3), ("neydarlysing", "Neyðarlýsing", 3), ("utgonguljos", "Neyðarlýsing", 3),
        ("bordi", "LED-borðar og fylgihlutir", 3), ("ledbordi", "LED-borðar og fylgihlutir", 3), ("profill", "LED-borðar og fylgihlutir", 2), ("driver", "LED-borðar og fylgihlutir", 2),
        ("pera", "Perur og íhlutir", 3), ("perur", "Perur og íhlutir", 3), ("gu10", "Perur og íhlutir", 3), ("e27", "Perur og íhlutir", 3), ("e14", "Perur og íhlutir", 3), ("g9", "Perur og íhlutir", 2), ("retro", "Perur og íhlutir", 1),
        ("snjallpera", "Snjallljós og perur", 3), ("hue", "Snjallljós og perur", 3),
        ("kastari", "Kastarar í brautir", 3), ("kastarabraut", "Kastarabrautir", 3), ("lampabraut", "Lampabrautir", 3), ("braut", "Kastarabrautir", 1),
        // Hitabúnaður
        ("hitastreng", "Hitastrengir", 3), ("hitakapall", "Hitastrengir", 3), ("hitamotta", "Hitamottur", 3), ("hitamottur", "Hitamottur", 3), ("golfhiti", "Hitamottur", 2),
        ("ofn", "Ofnar og hitakútar", 3), ("hitakutur", "Ofnar og hitakútar", 3), ("hitablasari", "Hitablásarar", 3), ("blasari", "Hitablásarar", 2), ("hitari", "Hitablásarar", 2),
        ("hitastillir", "Hitastillar", 3), ("thermostat", "Hitastillar", 3), ("termostat", "Hitastillar", 3), ("vifta", "Viftur og kæling", 3), ("viftur", "Viftur og kæling", 3),
        ("gluggastyring", "Gluggastýringar", 3), ("gluggaopnari", "Gluggastýringar", 3),
        // Fjarskiptabúnaður
        ("netkapall", "Netstrengir", 3), ("netstreng", "Netstrengir", 3), ("utp", "Netstrengir", 2), ("ftp", "Netstrengir", 1),
        ("rj", "Netefni", 2), ("rj45", "Netefni", 3), ("cat5", "Netefni", 3), ("cat6", "Netefni", 3), ("cat", "Netefni", 1), ("keystone", "Netefni", 3), ("patch", "Netefni", 2), ("netefni", "Netefni", 3),
        ("ljosleidari", "Ljósleiðarastrengir", 2), ("ljosleidarastreng", "Ljósleiðarastrengir", 3), ("ljosleidaratengi", "Ljósleiðaratengingar", 3), ("sc", "Ljósleiðaratengingar", 0.5), ("lc", "Ljósleiðaratengingar", 0.5),
        ("netskiptir", "Netskiptar", 3), ("switch", "Netskiptar", 1), ("poe", "Netskiptar", 2),
        ("wifi", "Þráðlaus netbúnaður", 1), ("thradlaus", "Þráðlaus netbúnaður", 3), ("access", "Þráðlaus netbúnaður", 1), ("router", "Þráðlaus netbúnaður", 3),
        ("rack", "19\" Tölvuskápar", 3), ("skipuleggjari", "19\" Tölvuskápar", 2), ("tolvuskapur", "19\" Tölvuskápar", 3), ("excel", "19\" Tölvuskápar", 1),
        ("fjarskiptamaelir", "Fjarskiptamælar", 3), ("brunakerfi", "Brunakerfi", 3), ("reykskynjari", "Brunakerfi", 3), ("brunavidvorun", "Brunakerfi", 3),
        // Second pass over a live list of 787: switch-range parts, conduit accessories, hardware, cable trade names
        ("hus", "Rofar og tenglar", 2), ("blindplata", "Rofar og tenglar", 3), ("thettisett", "Rofar og tenglar", 3), ("midja", "Rofar og tenglar", 2), ("samrofi", "Rofar og tenglar", 3),
        ("runpo", "Lagnaleiðir", 3), ("netstigi", "Lagnaleiðir", 3), ("bakki", "Lagnaleiðir", 2), ("bakkar", "Lagnaleiðir", 2), ("rorastoll", "Lagnaleiðir", 3), ("beygja", "Lagnaleiðir", 3),
        ("holkur", "Lagnaleiðir", 2), ("stutur", "Lagnaleiðir", 2), ("tengistutur", "Lagnaleiðir", 3), ("adfelluhringur", "Lagnaleiðir", 3),
        ("rv", "Aflstrengir", 1), ("powerflex", "Aflstrengir", 3), ("eldbodstr", "Merkjastrengir", 3), ("eldbod", "Merkjastrengir", 3), ("jy", "Merkjastrengir", 2), ("liyy", "Merkjastrengir", 3),
        ("raer", "Festingar", 3), ("stalro", "Festingar", 3), ("messingro", "Festingar", 3), ("plastro", "Festingar", 3), ("skinnur", "Festingar", 3), ("brettaskinnur", "Festingar", 3),
        ("murhulsa", "Festingar", 3), ("stoppari", "Festingar", 2), ("dragbind", "Festingar", 3), ("kapalspenna", "Festingar", 3),
        ("neozed", "Töflubúnaður", 3), ("mathulsa", "Töflubúnaður", 3), ("botnhringur", "Töflubúnaður", 3), ("nullskinnuhalda", "Töflubúnaður", 3), ("safnskinna", "Töflubúnaður", 3),
        ("gaffall", "Tengiefni", 3), ("endastopp", "Tengiefni", 3), ("clipfix", "Tengiefni", 3), ("virendah", "Tengiefni", 3), ("virendahulsa", "Tengiefni", 3), ("afltengi", "Tengiefni", 3),
        ("viratengi", "Tengiefni", 3), ("innatengi", "Tengiefni", 3), ("samtengi", "Tengiefni", 3), ("straumtengi", "Tengiefni", 3), ("tengikl", "Tengiefni", 3),
        ("lumen", "Almenn lýsing", 2), ("hofflights", "Almenn lýsing", 2), ("dualtone", "Almenn lýsing", 2), ("libertad", "Almenn lýsing", 2),
        ("firestop", "Efni og lím", 3), ("brunathetti", "Efni og lím", 3), ("thettimassi", "Efni og lím", 3),
        ("skurdarskifa", "Verkfæri", 3), ("toppur", "Verkfæri", 2), ("trappa", "Verkfæri", 3), ("strekkjari", "Verkfæri", 3), ("pica", "Verkfæri", 3), ("afylling", "Verkfæri", 1),
        ("hillurekki", "Rekstrarvörur", 3), ("rekki", "Rekstrarvörur", 2),
        ("dragbond", "Festingar", 3), ("murhulsur", "Festingar", 3), ("holkar", "Lagnaleiðir", 2), ("trodnipp", "Lagnaleiðir", 3), ("nipplar", "Lagnaleiðir", 2),
        ("afllgj", "Aflgjafar", 3), ("safnskinnu", "Töflubúnaður", 3), ("radtengjaeining", "Töflubúnaður", 3), ("eining", "Töflubúnaður", 1),
        ("skralllykill", "Verkfæri", 3), ("skralllyklasett", "Verkfæri", 3), ("topplyklasett", "Verkfæri", 3), ("lykill", "Verkfæri", 2), ("lyklasett", "Verkfæri", 3),
        ("skrufbita", "Verkfæri", 3), ("bitasett", "Verkfæri", 3), ("snigilbor", "Verkfæri", 3), ("hjolsagarbl", "Verkfæri", 3), ("sagarblad", "Verkfæri", 3), ("linulaser", "Verkfæri", 3),
        ("laser", "Verkfæri", 3), ("smidabukki", "Verkfæri", 3), ("spennupenni", "Verkfæri", 3), ("fluke", "Verkfæri", 3), ("segulpenni", "Verkfæri", 3), ("skabitur", "Verkfæri", 3),
        ("vinnuvettling", "Vinnufatnaður", 3), ("vettling", "Vinnufatnaður", 3), ("hnjapud", "Vinnufatnaður", 3),
        ("fraud", "Efni og lím", 3), ("sikaboom", "Efni og lím", 3), ("tubur", "Efni og lím", 2), ("monster", "Rekstrarvörur", 3),
        // Workit's own
        ("kitti", "Efni og lím", 3), ("lim", "Efni og lím", 3), ("feiti", "Efni og lím", 3), ("sprey", "Efni og lím", 3), ("spray", "Efni og lím", 3), ("silikon", "Efni og lím", 3),
        ("eldvarnar", "Efni og lím", 2), ("brunathetting", "Efni og lím", 3),
        ("jakki", "Vinnufatnaður", 3), ("bolur", "Vinnufatnaður", 3), ("buxur", "Vinnufatnaður", 3), ("hettu", "Vinnufatnaður", 2), ("peysa", "Vinnufatnaður", 3), ("hanskar", "Vinnufatnaður", 3),
        ("skor", "Vinnufatnaður", 2), ("vesti", "Vinnufatnaður", 3), ("hufa", "Vinnufatnaður", 3), ("vinnufat", "Vinnufatnaður", 3), ("hjalm", "Vinnufatnaður", 3), ("gleraugu", "Vinnufatnaður", 3),
        ("ruslapok", "Rekstrarvörur", 3), ("pappir", "Rekstrarvörur", 2), ("hreinsi", "Rekstrarvörur", 3), ("klutur", "Rekstrarvörur", 3),
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
            // A lumen figure is a light, whatever the number; other measurements say nothing.
            if (raw.Length > 2 && raw.EndsWith("lm", StringComparison.Ordinal) && raw[..^2].All(char.IsDigit)) { yield return "lumen"; continue; }
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
