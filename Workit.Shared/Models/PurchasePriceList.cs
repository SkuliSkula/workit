namespace Workit.Shared.Models;

/// <summary>A product whose Payday price would rise to what it was last bought at.</summary>
public sealed record PriceRaise(Guid PaydayProductId, string Sku, string Name, decimal CurrentPriceExVat, decimal NewPriceExVat,
    string Vendor, DateTime? PurchasedOn, bool LooksLikeCharge = false);

/// <summary>An item bought but not in the Payday price list, to be created there.</summary>
public sealed record PriceListAddition(string Sku, string Name, decimal PriceExVat, decimal VatPercentage, string Vendor, DateTime? PurchasedOn,
    bool LooksLikeCharge = false);

/// <summary>
/// What keeping the price list up to date from purchases would do: raise these
/// prices, create these products. <see cref="ArchivedSkus"/> are items bought whose
/// product is archived in Payday — left alone, never re-created.
/// </summary>
public sealed record PriceListPlan(
    List<PriceRaise> Raises,
    List<PriceListAddition> Additions,
    List<string> ArchivedSkus,
    DateTimeOffset? Since,
    string? Problem = null,
    int IgnoredCount = 0);

/// <summary>Apply, leaving out these item numbers — now and in every later run.</summary>
public sealed record PriceListApplyRequest(List<string> Ignore);

/// <summary>What applying a plan did. <see cref="Failed"/> holds Payday's message per item that it refused.</summary>
public sealed record PriceListApplyResult(int Raised, int Created, List<string> Failed, DateTimeOffset Since);
