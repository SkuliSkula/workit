using Microsoft.EntityFrameworkCore;
using Workit.Shared.Models;

namespace Workit.Api.Data;

public static class SeedData
{
    public static async Task EnsureSeededAsync(WorkitDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        if (await dbContext.Companies.AnyAsync(cancellationToken))
        {
            return;
        }

        var company = new Company { Name = "Demo Construction" };
        var customer = new Customer { CompanyId = company.Id, Name = "Contoso Facilities" };
        var employee = new Employee { CompanyId = company.Id, DisplayName = "Alex Carpenter", Trade = "Carpenter" };
        var job = new Job
        {
            CompanyId = company.Id,
            CustomerId = customer.Id,
            Name = "Warehouse Fit-Out",
            Code = "WH-1001"
        };
        var entry = new TimeEntry
        {
            CompanyId = company.Id,
            JobId = job.Id,
            EmployeeId = employee.Id,
            WorkDate = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Hours = 8m,
            Notes = "Initial demo entry"
        };

        await dbContext.AddRangeAsync(company, customer, employee, job, entry, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
