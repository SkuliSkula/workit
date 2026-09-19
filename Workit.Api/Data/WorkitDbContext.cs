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
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<MaterialUsage> MaterialUsages => Set<MaterialUsage>();
    public DbSet<VendorInvoice> VendorInvoices => Set<VendorInvoice>();
    public DbSet<VendorInvoiceLineItem> VendorInvoiceLineItems => Set<VendorInvoiceLineItem>();
    public DbSet<EmailSettings> EmailSettings => Set<EmailSettings>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AbsenceRequest> AbsenceRequests => Set<AbsenceRequest>();
    public DbSet<UserCompany> UserCompanies => Set<UserCompany>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Invoice>        Invoices        => Set<Invoice>();
    public DbSet<InvoiceLine>    InvoiceLines    => Set<InvoiceLine>();
    public DbSet<InvoicePayment> InvoicePayments => Set<InvoicePayment>();
    public DbSet<Expense>            Expenses           => Set<Expense>();
    public DbSet<ExpenseLine>        ExpenseLines       => Set<ExpenseLine>();
    public DbSet<ExpenseLineBilling> ExpenseLineBillings => Set<ExpenseLineBilling>();
    public DbSet<JobAttachment>      JobAttachments     => Set<JobAttachment>();
    public DbSet<JobTask>            JobTasks           => Set<JobTask>();
    public DbSet<PaydayProductCache> PaydayProducts     => Set<PaydayProductCache>();

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
        modelBuilder.Entity<Material>().ToTable("Materials");
        modelBuilder.Entity<MaterialUsage>().ToTable("MaterialUsages");
        modelBuilder.Entity<VendorInvoice>().ToTable("VendorInvoices");
        modelBuilder.Entity<VendorInvoiceLineItem>().ToTable("VendorInvoiceLineItems");
        modelBuilder.Entity<EmailSettings>().ToTable("EmailSettings");

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

        modelBuilder.Entity<Customer>()
            .HasIndex(x => new { x.CompanyId, x.PaydayId })
            .HasFilter("\"PaydayId\" IS NOT NULL");

        modelBuilder.Entity<Employee>()
            .HasIndex(x => new { x.CompanyId, x.DisplayName })
            .IsUnique(false);

        modelBuilder.Entity<Employee>()
            .HasIndex(x => new { x.CompanyId, x.PaydayId })
            .HasFilter("\"PaydayId\" IS NOT NULL");

        modelBuilder.Entity<Company>()
            .HasIndex(x => x.PaydayId)
            .HasFilter("\"PaydayId\" IS NOT NULL");

        // Response-only flag; the encrypted credential columns are the source of truth.
        modelBuilder.Entity<Company>()
            .Ignore(x => x.HasPaydayCredentials);

        modelBuilder.Entity<Job>()
            .HasIndex(x => new { x.CompanyId, x.Code })
            .IsUnique(false);

        // JobNumber is a per-company counter allocated as MAX + 1 on create.
        // The index makes a concurrent double allocation fail instead of
        // producing two jobs with the same number and code; CreateJob retries.
        modelBuilder.Entity<Job>()
            .HasIndex(x => new { x.CompanyId, x.JobNumber })
            .IsUnique();

        modelBuilder.Entity<TimeEntry>()
            .HasIndex(x => new { x.CompanyId, x.EmployeeId, x.WorkDate });

        modelBuilder.Entity<Tool>()
            .HasIndex(x => new { x.CompanyId, x.Name });

        modelBuilder.Entity<ToolAssignment>()
            .HasIndex(x => new { x.CompanyId, x.ToolId });

        modelBuilder.Entity<ToolAssignment>()
            .HasIndex(x => new { x.CompanyId, x.EmployeeId });

        modelBuilder.Entity<Material>()
            .HasIndex(x => new { x.CompanyId, x.Category });

        modelBuilder.Entity<Material>()
            .HasIndex(x => new { x.CompanyId, x.ProductCode });

        modelBuilder.Entity<MaterialUsage>()
            .HasIndex(x => new { x.CompanyId, x.MaterialId });

        modelBuilder.Entity<MaterialUsage>()
            .HasIndex(x => new { x.CompanyId, x.EmployeeId });

        // VendorInvoices: dedup on (CompanyId, SourceEmailMessageId)
        modelBuilder.Entity<VendorInvoice>()
            .HasIndex(x => new { x.CompanyId, x.SourceEmailMessageId });

        modelBuilder.Entity<VendorInvoice>()
            .HasIndex(x => new { x.CompanyId, x.InvoiceDate });

        modelBuilder.Entity<VendorInvoiceLineItem>()
            .HasIndex(x => new { x.CompanyId, x.InvoiceId });

        modelBuilder.Entity<VendorInvoiceLineItem>()
            .HasIndex(x => new { x.CompanyId, x.ProductCode });

        // EmailSettings: one row per company
        modelBuilder.Entity<EmailSettings>()
            .HasIndex(x => x.CompanyId)
            .IsUnique();

        modelBuilder.Entity<AbsenceRequest>().ToTable("AbsenceRequests");
        modelBuilder.Entity<AbsenceRequest>()
            .HasIndex(x => new { x.CompanyId, x.EmployeeId, x.StartDate });
        modelBuilder.Entity<AbsenceRequest>()
            .HasIndex(x => new { x.CompanyId, x.Status });

        modelBuilder.Entity<RefreshToken>().ToTable("RefreshTokens");
        modelBuilder.Entity<RefreshToken>()
            .HasIndex(x => x.Token)
            .IsUnique();
        modelBuilder.Entity<RefreshToken>()
            .HasIndex(x => x.UserId);

        modelBuilder.Entity<UserCompany>(e =>
        {
            e.ToTable("UserCompanies");
            e.HasIndex(x => new { x.UserId, x.CompanyId }).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<PasswordResetToken>().ToTable("PasswordResetTokens");
        modelBuilder.Entity<PasswordResetToken>()
            .HasIndex(x => x.Token)
            .IsUnique();
        modelBuilder.Entity<PasswordResetToken>()
            .HasIndex(x => x.Email);

        // ── Invoices ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Invoice>().ToTable("Invoices");
        modelBuilder.Entity<Invoice>()
            .HasIndex(x => new { x.CompanyId, x.Status });
        modelBuilder.Entity<Invoice>()
            .HasIndex(x => new { x.CompanyId, x.InvoiceDate });
        modelBuilder.Entity<Invoice>()
            .HasIndex(x => new { x.CompanyId, x.PaydayId })
            .HasFilter("\"PaydayId\" IS NOT NULL");
        modelBuilder.Entity<Invoice>()
            .HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Invoice>()
            .HasMany(x => x.Payments)
            .WithOne()
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InvoiceLine>().ToTable("InvoiceLines");
        modelBuilder.Entity<InvoiceLine>()
            .HasIndex(x => new { x.CompanyId, x.InvoiceId });

        modelBuilder.Entity<InvoicePayment>().ToTable("InvoicePayments");
        modelBuilder.Entity<InvoicePayment>()
            .HasIndex(x => new { x.CompanyId, x.InvoiceId });

        // ── Expenses ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Expense>().ToTable("Expenses");
        modelBuilder.Entity<Expense>()
            .HasIndex(x => new { x.CompanyId, x.Status });
        modelBuilder.Entity<Expense>()
            .HasIndex(x => new { x.CompanyId, x.Date });
        modelBuilder.Entity<Expense>()
            .HasIndex(x => new { x.CompanyId, x.PaydayId })
            .HasFilter("\"PaydayId\" IS NOT NULL");
        modelBuilder.Entity<Expense>()
            .HasIndex(x => new { x.CompanyId, x.JobId })
            .HasFilter("\"JobId\" IS NOT NULL");
        modelBuilder.Entity<Expense>()
            .HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExpenseLine>().ToTable("ExpenseLines");
        modelBuilder.Entity<ExpenseLine>()
            .HasIndex(x => new { x.CompanyId, x.ExpenseId });
        modelBuilder.Entity<ExpenseLine>()
            .HasMany(x => x.Billings)
            .WithOne()
            .HasForeignKey(x => x.ExpenseLineId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExpenseLineBilling>().ToTable("ExpenseLineBillings");
        modelBuilder.Entity<ExpenseLineBilling>()
            .HasIndex(x => new { x.CompanyId, x.ExpenseLineId });
        modelBuilder.Entity<ExpenseLineBilling>()
            .HasIndex(x => new { x.CompanyId, x.JobId })
            .HasFilter("\"JobId\" IS NOT NULL");

        // ── Job tasks ─────────────────────────────────────────────────────────
        modelBuilder.Entity<JobTask>().ToTable("JobTasks");
        modelBuilder.Entity<JobTask>()
            .HasIndex(x => new { x.CompanyId, x.JobId });
        // TaskNumber is MAX + 1 within the job, allocated under a lock; the
        // index makes a concurrent double allocation fail instead of producing
        // two tasks with the same number and code.
        modelBuilder.Entity<JobTask>()
            .HasIndex(x => new { x.JobId, x.TaskNumber })
            .IsUnique();
        modelBuilder.Entity<TimeEntry>()
            .HasIndex(x => x.TaskId);

        // ── Payday product cache ──────────────────────────────────────────────
        modelBuilder.Entity<PaydayProductCache>().ToTable("PaydayProducts");
        modelBuilder.Entity<PaydayProductCache>()
            .HasIndex(x => new { x.CompanyId, x.PaydayId })
            .IsUnique();
        modelBuilder.Entity<PaydayProductCache>()
            .HasIndex(x => new { x.CompanyId, x.Sku });
        modelBuilder.Entity<PaydayProductCache>()
            .Ignore(x => x.IsStockTracked);

        // ── Job attachments ───────────────────────────────────────────────────
        modelBuilder.Entity<JobAttachment>().ToTable("JobAttachments");
        modelBuilder.Entity<JobAttachment>()
            .HasIndex(x => new { x.CompanyId, x.JobId });
    }
}
