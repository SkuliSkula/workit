using Microsoft.EntityFrameworkCore;
using Workit.Shared.Models;

namespace Workit.Api.Data;

public sealed class WorkitDbContext(DbContextOptions<WorkitDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<Tool> Tools => Set<Tool>();
    public DbSet<ToolAssignment> ToolAssignments => Set<ToolAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>().ToTable("AppUsers");
        modelBuilder.Entity<Company>().ToTable("Companies");
        modelBuilder.Entity<Customer>().ToTable("Customers");
        modelBuilder.Entity<Employee>().ToTable("Employees");
        modelBuilder.Entity<Job>().ToTable("Jobs");
        modelBuilder.Entity<TimeEntry>().ToTable("TimeEntries");
        modelBuilder.Entity<Tool>().ToTable("Tools");
        modelBuilder.Entity<ToolAssignment>().ToTable("ToolAssignments");

        modelBuilder.Entity<AppUser>()
            .HasIndex(x => x.Email)
            .IsUnique();

        modelBuilder.Entity<AppUser>()
            .HasIndex(x => x.EmployeeId)
            .IsUnique()
            .HasFilter("\"EmployeeId\" IS NOT NULL");

        modelBuilder.Entity<Customer>()
            .HasIndex(x => new { x.CompanyId, x.Name })
            .IsUnique(false);

        modelBuilder.Entity<Employee>()
            .HasIndex(x => new { x.CompanyId, x.DisplayName })
            .IsUnique(false);

        modelBuilder.Entity<Job>()
            .HasIndex(x => new { x.CompanyId, x.Code })
            .IsUnique(false);

        modelBuilder.Entity<TimeEntry>()
            .HasIndex(x => new { x.CompanyId, x.EmployeeId, x.WorkDate });

        modelBuilder.Entity<Tool>()
            .HasIndex(x => new { x.CompanyId, x.Name });

        modelBuilder.Entity<ToolAssignment>()
            .HasIndex(x => new { x.CompanyId, x.ToolId });

        modelBuilder.Entity<ToolAssignment>()
            .HasIndex(x => new { x.CompanyId, x.EmployeeId });
    }
}
