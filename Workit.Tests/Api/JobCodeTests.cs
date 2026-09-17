using FluentAssertions;
using Workit.Api.Endpoints;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// The job code is the category's short code plus the company's running job
/// number — nothing from the customer. The migration that rewrote existing
/// codes (SimplifyJobCodes) hard-codes the same mapping in SQL, so a change
/// here needs a matching migration.
/// </summary>
public class JobCodeTests
{
    [Theory]
    [InlineData(JobCategory.NewInstallation, 1,    "NI001")]
    [InlineData(JobCategory.Repair,          7,    "REP007")]
    [InlineData(JobCategory.InnerWork,       104,  "IW104")]
    [InlineData(JobCategory.Drawings,        106,  "DWG106")]
    [InlineData(JobCategory.Offer,           12,   "OFF012")]
    [InlineData(JobCategory.Maintenance,     4,    "MNT004")]
    [InlineData(JobCategory.Inspection,      99,   "INS099")]
    [InlineData(JobCategory.Consultation,    123,  "CON123")]
    public void Code_IsCategoryCodeThenThreeDigitNumber(JobCategory category, int number, string expected)
    {
        JobEndpoints.FormatJobCode(category, number).Should().Be(expected);
    }

    [Fact]
    public void Number_PastThreeDigits_IsNotTruncated()
    {
        JobEndpoints.FormatJobCode(JobCategory.Repair, 1234).Should().Be("REP1234");
    }

    [Fact]
    public void UnknownCategory_FallsBackToJob()
    {
        JobEndpoints.FormatJobCode((JobCategory)42, 5).Should().Be("JOB005");
    }
}
