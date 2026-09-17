using FluentAssertions;
using Workit.Shared.Models;
using Workit.Shared.Payday;

namespace Workit.Tests.Api;

/// <summary>
/// Recognising job codes and job-number references in the free text a vendor
/// or owner puts on an expense. A hit needs a matching job to exist.
/// </summary>
public class JobCodeMatcherTests
{
    private static readonly Job Rep107 = new() { Name = "Endurnýjun raflagna", Code = "REP107", JobNumber = 107, Category = JobCategory.Repair };
    private static readonly Job Mnt004 = new() { Name = "Viðhald", Code = "MNT004", JobNumber = 4, Category = JobCategory.Maintenance };
    private static readonly Job Ni012  = new() { Name = "Nýlögn", Code = "NI012", JobNumber = 12, Category = JobCategory.NewInstallation };
    private static readonly Job[] Jobs = [Rep107, Mnt004, Ni012];

    [Theory]
    [InlineData("REP107")]
    [InlineData("rep107")]
    [InlineData("REP 107")]
    [InlineData("REP-107")]
    [InlineData("Efni fyrir REP107, afhent 3. sept")]
    [InlineData("REP-BBH-107")]          // the pre-September-2026 code
    [InlineData("Verk 107")]
    [InlineData("verknr. 107")]
    [InlineData("Verknúmer: 107")]
    [InlineData("Job #107")]
    [InlineData("job no 107")]
    [InlineData("#107")]
    public void Finds_TheJob_FromCodeOrNumberReference(string text)
    {
        JobCodeMatcher.FindJobs([text], Jobs).Should().ContainSingle().Which.Should().BeSameAs(Rep107);
    }

    [Theory]
    [InlineData("REP108")]                // no such job
    [InlineData("MNT107")]                // number exists, category does not match
    [InlineData("INSTALLATION 2")]        // "INS" inside a word, digits not adjacent
    [InlineData("Kapall 107 m")]          // a bare number is not a reference
    [InlineData("")]
    [InlineData(null)]
    public void Ignores_TextThatIsNotAJobReference(string? text)
    {
        JobCodeMatcher.FindJobs([text], Jobs).Should().BeEmpty();
    }

    [Fact]
    public void ReturnsEveryDistinctJob_WhenSeveralAreNamed()
    {
        var found = JobCodeMatcher.FindJobs(["REP107 og MNT004", "sjá REP107"], Jobs);
        found.Select(j => j.Code).Should().Equal("REP107", "MNT004");
    }

    [Fact]
    public void UniqueJob_IsNullWhenAmbiguousOrAbsent()
    {
        JobCodeMatcher.FindUniqueJob(new PaydayExpense { Comments = "REP107 / MNT004" }, Jobs).Should().BeNull();
        JobCodeMatcher.FindUniqueJob(new PaydayExpense { Comments = "nothing here" }, Jobs).Should().BeNull();
        JobCodeMatcher.FindUniqueJob(new PaydayExpense { Reference = "NI012" }, Jobs).Should().BeSameAs(Ni012);
    }

    [Fact]
    public void LooksAtEveryTextField_IncludingLines()
    {
        var expense = new PaydayExpense
        {
            Reference = "12345",
            Lines = [new PaydayExpenseLine { Description = "Rofar og tenglar" }, new PaydayExpenseLine { Description = "Efni MNT004" }],
        };
        JobCodeMatcher.FindUniqueJob(expense, Jobs).Should().BeSameAs(Mnt004);
    }
}
