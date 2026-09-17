using System.Text.RegularExpressions;
using Workit.Shared.Models;

namespace Workit.Shared.Payday;

/// <summary>
/// Finds the jobs a piece of free text refers to — the vendor wrote the job
/// code on the invoice, the owner typed it into a Payday comment. Recognises
/// the code as the API writes it (<c>REP107</c>), forgiving spacing, hyphens
/// and case (<c>rep 107</c>, <c>REP-107</c>), the pre-September-2026 form
/// with customer initials (<c>REP-BBH-107</c>), and an explicit number
/// reference (<c>verk 107</c>, <c>verknr. 107</c>, <c>job #107</c>). A hit
/// only counts when a job with that number — and, for a code, that
/// category — exists, so stray text cannot link to anything.
/// </summary>
public static partial class JobCodeMatcher
{
    // Category code, optional legacy customer initials, then the number.
    // The look-behind keeps "INS" from matching inside "BINS 3"; the digits
    // must follow directly or after one space/hyphen so "INSTALLATION 2" is
    // not a code.
    [GeneratedRegex(@"(?<![\p{L}\d])(NI|REP|IW|DWG|OFF|MNT|INS|CON|JOB)(?:[ \-][\p{Lu}]{1,4})?[ \-]?(\d{1,5})(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    // "verk 107", "verknr. 107", "verknúmer: 107", "job 107", "job no 107", "#107".
    [GeneratedRegex(@"(?:(?<![\p{L}])verk(?:nr|númer|numer|efni)?\.?|(?<![\p{L}])job(?:\s*(?:no|nr|number))?\.?|#)\s*[:.]?\s*(\d{1,5})(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    /// <summary>The distinct jobs referred to across all <paramref name="texts"/>, in first-seen order.</summary>
    public static IReadOnlyList<Job> FindJobs(IEnumerable<string?> texts, IReadOnlyCollection<Job> jobs)
    {
        var found = new List<Job>();
        var byNumber = jobs.GroupBy(j => j.JobNumber).ToDictionary(g => g.Key, g => g.ToList());

        void Add(Job job)
        {
            if (!found.Any(f => f.Id == job.Id)) found.Add(job);
        }

        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;

            foreach (Match m in CodePattern().Matches(text))
            {
                var categoryCode = m.Groups[1].Value.ToUpperInvariant();
                var number = int.Parse(m.Groups[2].Value);
                if (!byNumber.TryGetValue(number, out var candidates)) continue;
                foreach (var job in candidates.Where(j => JobCodes.CategoryCode(j.Category) == categoryCode))
                    Add(job);
            }

            foreach (Match m in NumberPattern().Matches(text))
            {
                var number = int.Parse(m.Groups[1].Value);
                if (!byNumber.TryGetValue(number, out var candidates)) continue;
                foreach (var job in candidates) Add(job);
            }
        }

        return found;
    }

    /// <summary>Every text on a Payday expense a vendor or owner could have written a job code into.</summary>
    public static IEnumerable<string?> TextsOf(PaydayExpense expense)
    {
        yield return expense.Reference;
        yield return expense.Comments;
        yield return expense.Voucher;
        foreach (var line in expense.Lines ?? [])
            yield return line.Description;
    }

    /// <summary>The single job an expense unambiguously refers to, or null when there are none or several.</summary>
    public static Job? FindUniqueJob(PaydayExpense expense, IReadOnlyCollection<Job> jobs)
    {
        var found = FindJobs(TextsOf(expense), jobs);
        return found.Count == 1 ? found[0] : null;
    }
}
