using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Workit.Api.Endpoints;

namespace Workit.Tests.Api;

/// <summary>
/// The pieces of job-number allocation that can be checked without a
/// database: which save failures count as "someone took that number", and
/// the per-company key the advisory lock is taken on. The allocation itself
/// — MAX + 1 under the lock, unique index as backstop — needs Postgres.
/// </summary>
public class JobNumberTests
{
    [Fact]
    public void UniqueViolation_IsRecognised()
    {
        var pg = new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);
        JobEndpoints.IsUniqueViolation(new DbUpdateException("save failed", pg)).Should().BeTrue();
    }

    [Theory]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData(PostgresErrorCodes.NotNullViolation)]
    public void OtherPostgresErrors_AreNotRetried(string sqlState)
    {
        var pg = new PostgresException("nope", "ERROR", "ERROR", sqlState);
        JobEndpoints.IsUniqueViolation(new DbUpdateException("save failed", pg)).Should().BeFalse();
    }

    [Fact]
    public void NonPostgresFailure_IsNotRetried()
    {
        JobEndpoints.IsUniqueViolation(new DbUpdateException("save failed", new TimeoutException())).Should().BeFalse();
        JobEndpoints.IsUniqueViolation(new DbUpdateException("save failed")).Should().BeFalse();
    }

    [Fact]
    public void LockKey_IsStablePerCompanyAndDiffersBetweenCompanies()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        JobEndpoints.JobNumberLockKey(a).Should().Be(JobEndpoints.JobNumberLockKey(a));
        JobEndpoints.JobNumberLockKey(a).Should().NotBe(JobEndpoints.JobNumberLockKey(b));
    }
}
