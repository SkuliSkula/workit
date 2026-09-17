namespace Workit.Shared.Models;

/// <summary>
/// The job code: the category's short code and the company's running job
/// number, e.g. <c>REP007</c>. Assigned by the API on create, never edited.
/// Lives here so clients can recognise codes in free text the same way the
/// API writes them.
/// </summary>
public static class JobCodes
{
    public static string Format(JobCategory category, int jobNumber) =>
        $"{CategoryCode(category)}{jobNumber:D3}";

    public static string CategoryCode(JobCategory category) => category switch
    {
        JobCategory.NewInstallation => "NI",
        JobCategory.Repair          => "REP",
        JobCategory.InnerWork       => "IW",
        JobCategory.Drawings        => "DWG",
        JobCategory.Offer           => "OFF",
        JobCategory.Maintenance     => "MNT",
        JobCategory.Inspection      => "INS",
        JobCategory.Consultation    => "CON",
        _                           => "JOB",
    };

    /// <summary>Every category code the matcher should recognise, including the fallback.</summary>
    public static readonly IReadOnlyList<string> AllCategoryCodes =
        ["NI", "REP", "IW", "DWG", "OFF", "MNT", "INS", "CON", "JOB"];
}
