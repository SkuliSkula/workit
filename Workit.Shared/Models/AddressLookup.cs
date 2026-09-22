namespace Workit.Shared.Models;

/// <summary>
/// One address from Staðfangaskrá, HMS's national address registry.
/// <see cref="Display"/> is what goes into <see cref="Job.Location"/>
/// ("Borgartún 26, 105 Reykjavík"); the coordinates are WGS84.
/// </summary>
public sealed record AddressHit(
    long HnitNum,
    int? LandNr,
    string Display,
    string Street,
    string HouseNumber,
    int? PostCode,
    string Town,
    double Latitude,
    double Longitude);

/// <summary>
/// Search result. <see cref="LookupAvailable"/> is false when the registry
/// could not be reached, so the UI can say so instead of showing "no matches".
/// </summary>
public sealed record AddressSearchResult(IReadOnlyList<AddressHit> Hits, bool LookupAvailable);

/// <summary>
/// A contact worth offering for a job's "Contact on site": someone entered
/// on an earlier job at the same address or parcel, or the customer's own
/// contact person. <see cref="Source"/> says which ("MNT102 · 12 Sep" or
/// "Customer").
/// </summary>
public sealed record ContactSuggestion(string Name, string Phone, string Source);
