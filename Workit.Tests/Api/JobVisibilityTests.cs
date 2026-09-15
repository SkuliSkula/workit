using FluentAssertions;
using Workit.Api.Auth;
using Workit.Api.Endpoints;
using Workit.Shared.Auth;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

/// <summary>
/// Which jobs an employee may log work against. The apps filter their pickers
/// by this rule and the API enforces it on writes, so the two must agree — and
/// the rule has to stay: unassigned jobs are shared, assigned ones are not.
/// </summary>
public class JobVisibilityTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Colleague = Guid.NewGuid();

    private static UserContext Employee(Guid? employeeId = null) => new()
    {
        UserId = Guid.NewGuid(), CompanyId = Guid.NewGuid(), EmployeeId = employeeId ?? Me, Role = WorkitRoles.Employee,
    };

    private static Job JobAssignedTo(params Guid[] ids) => new() { AssignedEmployeeIds = [.. ids] };

    [Fact]
    public void Employee_MayUseAnUnassignedJob()
    {
        Employee().CanUseJob(JobAssignedTo()).Should().BeTrue();
    }

    [Fact]
    public void Employee_MayUseAJobTheyAreAssignedTo()
    {
        Employee().CanUseJob(JobAssignedTo(Colleague, Me)).Should().BeTrue();
    }

    [Fact]
    public void Employee_MayNotUseAJobAssignedOnlyToOthers()
    {
        Employee().CanUseJob(JobAssignedTo(Colleague)).Should().BeFalse();
    }

    /// <summary>A login with no employee record cannot be "assigned", so it sees only shared jobs.</summary>
    [Fact]
    public void EmployeeWithoutEmployeeRecord_SeesOnlySharedJobs()
    {
        var user = new UserContext { UserId = Guid.NewGuid(), CompanyId = Guid.NewGuid(), EmployeeId = null, Role = WorkitRoles.Employee };
        user.CanUseJob(JobAssignedTo()).Should().BeTrue();
        user.CanUseJob(JobAssignedTo(Colleague)).Should().BeFalse();
    }

    [Theory]
    [InlineData(WorkitRoles.Owner)]
    [InlineData(WorkitRoles.Admin)]
    public void OwnersAndAdmins_MayUseAnyJob(string role)
    {
        var user = new UserContext { UserId = Guid.NewGuid(), CompanyId = Guid.NewGuid(), EmployeeId = null, Role = role };
        user.CanUseJob(JobAssignedTo(Colleague)).Should().BeTrue();
    }
}
