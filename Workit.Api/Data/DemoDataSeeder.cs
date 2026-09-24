using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Shared.Auth;
using Workit.Shared.Models;

namespace Workit.Api.Data;

/// <summary>
/// Seeds a self-contained demo company, used for Apple App Store review and for
/// capturing App Store screenshots.
///
/// The data is entirely fictional and lives in its own company, so a reviewer
/// signing in never sees a real customer's employees, wages or national IDs.
/// Every person and company below is invented; the surnames deliberately
/// contain "Demo" so the records cannot be mistaken for real ones.
///
/// Run it with:  dotnet run --project Workit.Api -- --seed-demo
///
/// It is idempotent: re-running deletes the demo company's data and rebuilds
/// it, so the three-month window always ends on the day it is run. Nothing
/// outside the demo company is touched.
/// </summary>
public static class DemoDataSeeder
{
    public const string CompanyName   = "Workit Demo ehf.";
    public const string OwnerEmail    = "demo@workit.is";
    public const string EmployeeEmail = "demo.employee@workit.is";

    /// <summary>Shared by both demo logins. Must never be rotated — Apple keeps it on file.</summary>
    public const string Password = "WorkitDemo2026!";

    /// <summary>
    /// True for the two demo logins filed with Apple App Review.
    ///
    /// Their password is published in App Store Connect, so anything that would
    /// change it has to be refused: a single reset — ours while testing, or a
    /// reviewer's — locks the review team out of the app with no warning, and
    /// the failure only surfaces as a rejected build.
    /// </summary>
    public static bool IsProtectedAccount(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var normalized = email.Trim();
        return normalized.Equals(OwnerEmail, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(EmployeeEmail, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<string> SeedAsync(WorkitDbContext db, CancellationToken ct = default)
    {
        var existing = await db.Companies.FirstOrDefaultAsync(c => c.Name == CompanyName, ct);
        if (existing is not null)
        {
            await PurgeAsync(db, existing.Id, ct);
        }

        var rng   = new Random(20260909);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var start = today.AddMonths(-3);

        // ── Company ───────────────────────────────────────────────────────────
        var company = new Company
        {
            Id                  = Guid.NewGuid(),
            Name                = CompanyName,
            Ssn                 = "5012101239",
            Email               = "demo@workit.is",
            Phone               = "5550100",
            Address             = "Demógata 1",
            ZipCode             = "105",
            City                = "Reykjavík",
            Owner               = "Ólafur Demóson",
            DrivingUnitPrice    = 350m,
            StandardHoursPerDay = 8m,
        };
        db.Companies.Add(company);

        // ── Employees ─────────────────────────────────────────────────────────
        var employeeDefs = new (string Name, string Trade, string Ssn, decimal Salary, decimal Rate)[]
        {
            ("Ólafur Demóson",     "Rafvirkjameistari", "0101803339", 4200m, 7200m),
            ("Anna Demósdóttir",   "Rafvirki",          "1202854449", 3600m, 6400m),
            ("Baldur Demóson",     "Rafvirki",          "2303905559", 3400m, 6200m),
            ("Katrín Demósdóttir", "Nemi",              "3004956669", 2400m, 4800m),
            ("Sigurður Demóson",   "Pípari",            "0405907779", 3800m, 6800m),
        };

        var employees = new List<Employee>();
        foreach (var (name, trade, ssn, salary, rate) in employeeDefs)
        {
            employees.Add(new Employee
            {
                Id                 = Guid.NewGuid(),
                CompanyId          = company.Id,
                DisplayName        = name,
                Trade              = trade,
                Ssn                = ssn,
                Email              = $"{Slug(name)}@demo.workit.is",
                Phone              = $"555{rng.Next(1000, 9999)}",
                ContactPerson      = string.Empty,
                EmploymentType     = EmploymentType.Employed,
                HourlySalary       = salary,
                HourlyBillableRate = rate,
                Address            = "Demógata 1",
                ZipCode            = "105",
                City               = "Reykjavík",
                IsActive           = true,
            });
        }
        db.Employees.AddRange(employees);

        var ownerEmployee    = employees[0];
        var employeeForLogin = employees[1];

        // ── Logins ────────────────────────────────────────────────────────────
        // An Owner sees every tab, including the owner-only Jobs and Customers
        // tabs, so this is the account App Review should be given. The second
        // login exists to demonstrate the employee-only experience.
        var ownerUser = new AppUser
        {
            Id           = Guid.NewGuid(),
            Name         = ownerEmployee.DisplayName,
            Email        = OwnerEmail,
            PasswordHash = PasswordHasher.HashPassword(Password),
            Role         = WorkitRoles.Owner,
            CompanyId    = company.Id,
            EmployeeId   = ownerEmployee.Id,
        };
        var employeeUser = new AppUser
        {
            Id           = Guid.NewGuid(),
            Name         = employeeForLogin.DisplayName,
            Email        = EmployeeEmail,
            PasswordHash = PasswordHasher.HashPassword(Password),
            Role         = WorkitRoles.Employee,
            CompanyId    = company.Id,
            EmployeeId   = employeeForLogin.Id,
        };
        db.AppUsers.AddRange(ownerUser, employeeUser);
        db.UserCompanies.Add(new UserCompany { UserId = ownerUser.Id, CompanyId = company.Id });

        // ── Customers ─────────────────────────────────────────────────────────
        var customers = new[]
        {
            NewCustomer(company.Id, "Demó Fasteignir ehf.",   "4501101119", "Hrafnhildur Demósdóttir"),
            NewCustomer(company.Id, "Norðurljós Verk ehf.",   "5602202229", "Gunnar Demóson"),
            NewCustomer(company.Id, "Bláfjall Byggingar hf.", "6703303339", "Elín Demósdóttir"),
            NewCustomer(company.Id, "Sæbraut Hótel ehf.",     "7804404449", "Kristján Demóson"),
        };
        db.Customers.AddRange(customers);

        // ── Jobs ──────────────────────────────────────────────────────────────
        var jobDefs = new (string Name, string Code, int Number, Customer Cust, JobCategory Cat, BillingType Bill)[]
        {
            ("Nýbygging – Kópavogstún 4",  "NI101", 101, customers[0], JobCategory.NewInstallation, BillingType.Hourly),
            ("Viðhald – skrifstofuhúsnæði", "MNT102", 102, customers[1], JobCategory.Maintenance,     BillingType.Hourly),
            ("Endurnýjun raflagna",         "REP103", 103, customers[2], JobCategory.Repair,          BillingType.FixedPrice),
            ("Hótel Sæbraut – lýsing",      "IW104", 104, customers[3], JobCategory.InnerWork,       BillingType.Hourly),
            ("Bilanaþjónusta",              "REP105", 105, customers[0], JobCategory.Repair,          BillingType.Hourly),
            ("Teikningar og úttekt",        "DWG106", 106, customers[2], JobCategory.Drawings,        BillingType.FixedPrice),
        };

        var jobs = jobDefs.Select(d => new Job
        {
            Id           = Guid.NewGuid(),
            CompanyId    = company.Id,
            CustomerId   = d.Cust.Id,
            Name         = d.Name,
            Code         = d.Code,
            JobNumber    = d.Number,
            Category     = d.Cat,
            BillingType  = d.Bill,
            KanbanStatus = KanbanStatus.Active,
        }).ToList();
        db.Jobs.AddRange(jobs);

        // ── Materials ─────────────────────────────────────────────────────────
        var materialDefs = new (string Name, string Code, string Category, string Unit, decimal Purchase, decimal Qty)[]
        {
            ("N1XE-U 5G 16 Cu 1kV aflstrengur", "N1XE5G16",  "Rafstrengir",  "m.",   1480m, 320m),
            ("N1XE-U 3G 2,5 Cu jarðstrengur",   "N1XE3G25",  "Rafstrengir",  "m.",    420m, 640m),
            ("Tengidós 65 mm",                  "TD065",     "Dósir",        "stk.",  180m, 240m),
            ("Innfelld loftljós LED 18W",       "LED18W",    "Lýsing",       "stk.", 3400m,  86m),
            ("Rofi einpóla hvítur",             "ROF1P",     "Rofar",        "stk.",  640m, 150m),
            ("Tengill tvöfaldur jarðtengdur",   "TEN2J",     "Tenglar",      "stk.",  890m, 130m),
            ("Sjálfvar 16A C-kúrfa",            "SJV16C",    "Varnarbúnaður","stk.", 1950m,  74m),
            ("Lekaliði 40A 30mA",               "LEK4030",   "Varnarbúnaður","stk.", 7400m,  22m),
            ("Kapalrenna 60x40 hvít",           "KR6040",    "Rennur",       "m.",    760m, 180m),
            ("Festiklemma 16 mm",               "FK016",     "Festingar",    "pk.",   320m,  95m),
            ("Rörkubbur 20 mm",                 "RK020",     "Lagnaefni",    "stk.",  145m, 300m),
            ("Einangrunarband svart",           "EBSV",      "Smávara",      "rúll.", 210m, 120m),
        };

        var materials = materialDefs.Select(d => new Material
        {
            Id            = Guid.NewGuid(),
            CompanyId     = company.Id,
            Name          = d.Name,
            ProductCode   = d.Code,
            Category      = d.Category,
            Unit          = d.Unit,
            Quantity      = d.Qty,
            PurchasePrice = d.Purchase,
            MarkupFactor  = 1.5m,
            UnitPrice     = decimal.Round(d.Purchase * 1.5m, 0),
            VatRate       = 24.0m,
            IsActive      = true,
            CreatedAt     = UtcAt(start, 8),
        }).ToList();
        db.Materials.AddRange(materials);

        // ── Tools ─────────────────────────────────────────────────────────────
        var toolDefs = new (string Name, string Description, string Serial)[]
        {
            ("Bosch GBH 2-28 borvél",        "Höggborvél með SDS-plus",     "BSH-228-0041"),
            ("Fluke 1664 FC mælitæki",       "Raflagnamælir",               "FLK-1664-0112"),
            ("Makita DHP486 herslutæki",     "18V rafhlöðuknúið",           "MKT-486-0233"),
            ("Hilti PM 40-MG línulaser",     "Grænn krosslínulaser",        "HLT-40MG-0077"),
            ("Kaperunarklippur 35 mm",       "Fyrir aflstrengi",            "KLP-035-0198"),
            ("Stigi 3 m álstigi",            "Þrepstigi",                   "STG-300-0154"),
            ("Snúruhjól 50 m",               "Framlengingarsnúra á hjóli",  "SNH-050-0261"),
        };

        var tools = toolDefs.Select(d => new Tool
        {
            Id           = Guid.NewGuid(),
            CompanyId    = company.Id,
            Name         = d.Name,
            Description  = d.Description,
            SerialNumber = d.Serial,
            CreatedAt    = UtcAt(start, 8),
        }).ToList();
        db.Tools.AddRange(tools);

        // Most tools are out with someone; two have been returned.
        var assignments = new List<ToolAssignment>();
        for (var i = 0; i < tools.Count; i++)
        {
            var assignedOn = start.AddDays(rng.Next(0, 20));
            var returned   = i >= tools.Count - 2;
            assignments.Add(new ToolAssignment
            {
                Id         = Guid.NewGuid(),
                ToolId     = tools[i].Id,
                CompanyId  = company.Id,
                EmployeeId = employees[i % employees.Count].Id,
                AssignedAt = UtcAt(assignedOn, 8),
                ReturnedAt = returned ? UtcAt(assignedOn.AddDays(rng.Next(10, 30)), 16) : null,
            });
        }
        db.ToolAssignments.AddRange(assignments);

        // ── Time entries: every weekday across the three-month window ─────────
        var noteOptions = new[]
        {
            "", "", "", // most entries carry no note
            "Lagnavinna á 2. hæð",
            "Uppsetning á töflu",
            "Frágangur og prófanir",
            "Bilanaleit",
            "Efnisöflun og akstur",
            "Úttekt með eftirlitsmanni",
        };

        var timeEntries = new List<TimeEntry>();
        for (var day = start; day <= today; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;

            foreach (var emp in employees)
            {
                // Roughly one weekday in eight is not logged.
                if (rng.Next(0, 8) == 0) continue;

                var job      = jobs[rng.Next(jobs.Count)];
                var overtime = rng.Next(0, 10) < 2 ? rng.Next(1, 4) : 0;

                timeEntries.Add(new TimeEntry
                {
                    Id            = Guid.NewGuid(),
                    CompanyId     = company.Id,
                    JobId         = job.Id,
                    EmployeeId    = emp.Id,
                    WorkDate      = day,
                    Hours         = rng.Next(6, 10),
                    OvertimeHours = overtime,
                    DrivingUnits  = rng.Next(0, 10) < 4 ? rng.Next(1, 4) : 0,
                    Notes         = noteOptions[rng.Next(noteOptions.Length)],
                });
            }
        }
        db.TimeEntries.AddRange(timeEntries);

        // ── Material usage, always alongside the hours that consumed it ───────
        // Material is used while working, so every usage hangs off a real time
        // entry and takes its day, employee and job. Scattering them at random
        // produced days with material and no hours, and material "used" by
        // someone who was not on the job — which is not how a day looks.
        var usages = new List<MaterialUsage>();
        foreach (var entry in timeEntries.OrderBy(_ => rng.Next()).Take(60))
        {
            var material = materials[rng.Next(materials.Count)];
            usages.Add(new MaterialUsage
            {
                Id         = Guid.NewGuid(),
                CompanyId  = company.Id,
                MaterialId = material.Id,
                EmployeeId = entry.EmployeeId,
                JobId      = entry.JobId,
                Quantity   = material.Unit == "m." ? rng.Next(5, 40) : rng.Next(1, 10),
                UsedAt     = UtcAt(entry.WorkDate, 14),
                Notes      = string.Empty,
            });
        }
        db.MaterialUsages.AddRange(usages);

        // ── Absence requests: a spread of types and states ────────────────────
        // Includes one pending request so the reviewer sees an actionable item.
        var absences = new List<AbsenceRequest>
        {
            NewAbsence(company.Id, employees[1].Id, AbsenceType.Vacation,      AbsenceStatus.Approved, start.AddDays(12), start.AddDays(21), ownerUser.Id, "Sumarfrí"),
            NewAbsence(company.Id, employees[2].Id, AbsenceType.SickLeave,     AbsenceStatus.Approved, start.AddDays(30), start.AddDays(32), ownerUser.Id, ""),
            NewAbsence(company.Id, employees[3].Id, AbsenceType.StudyLeave,    AbsenceStatus.Approved, start.AddDays(45), start.AddDays(46), ownerUser.Id, "Námskeið"),
            NewAbsence(company.Id, employees[4].Id, AbsenceType.SickChildLeave,AbsenceStatus.Approved, start.AddDays(58), start.AddDays(58), ownerUser.Id, ""),
            NewAbsence(company.Id, employees[2].Id, AbsenceType.PersonalLeave, AbsenceStatus.Denied,   start.AddDays(64), start.AddDays(65), ownerUser.Id, "Fyrirvari of stuttur"),
            NewAbsence(company.Id, employees[1].Id, AbsenceType.Vacation,      AbsenceStatus.Pending,  today.AddDays(14), today.AddDays(21), null, "Vetrarfrí"),
        };
        db.AbsenceRequests.AddRange(absences);

        await db.SaveChangesAsync(ct);

        return $"""
            Demo data seeded.

              Company        {CompanyName}
              Window         {start:yyyy-MM-dd} to {today:yyyy-MM-dd}

              Owner login    {OwnerEmail} / {Password}
              Employee login {EmployeeEmail} / {Password}

              {employees.Count} employees, {customers.Length} customers, {jobs.Count} jobs,
              {materials.Count} materials, {tools.Count} tools,
              {timeEntries.Count} time entries, {usages.Count} material usages,
              {absences.Count} absence requests.
            """;
    }

    /// <summary>
    /// Deletes everything belonging to the demo company, and nothing else.
    /// Every query below is filtered on the demo company's id.
    /// </summary>
    private static async Task PurgeAsync(WorkitDbContext db, Guid companyId, CancellationToken ct)
    {
        var userIds = await db.AppUsers.Where(u => u.CompanyId == companyId).Select(u => u.Id).ToListAsync(ct);

        await db.TimeEntries.Where(x => x.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.MaterialUsages.Where(x => x.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.ToolAssignments.Where(x => x.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.AbsenceRequests.Where(x => x.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.Materials.Where(x => x.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.Tools.Where(x => x.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.Jobs.Where(x => x.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.Customers.Where(x => x.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.RefreshTokens.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync(ct);
        await db.UserCompanies.Where(x => x.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.AppUsers.Where(x => x.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.Employees.Where(x => x.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.Companies.Where(x => x.Id == companyId).ExecuteDeleteAsync(ct);
    }

    private static Customer NewCustomer(Guid companyId, string name, string ssn, string contact) => new()
    {
        Id            = Guid.NewGuid(),
        CompanyId     = companyId,
        Name          = name,
        Ssn           = ssn,
        Email         = $"{Slug(name)}@demo.workit.is",
        Phone         = "5550" + ssn[..3],
        ContactPerson = contact,
        Address       = "Demógata 2",
        ZipCode       = "105",
        City          = "Reykjavík",
        Country       = "Ísland",
    };

    private static AbsenceRequest NewAbsence(
        Guid companyId, Guid employeeId, AbsenceType type, AbsenceStatus status,
        DateOnly from, DateOnly to, Guid? reviewedBy, string notes) => new()
    {
        Id          = Guid.NewGuid(),
        CompanyId   = companyId,
        EmployeeId  = employeeId,
        Type        = type,
        Status      = status,
        StartDate   = from,
        EndDate     = to,
        Notes       = notes,
        ReviewedBy  = status == AbsenceStatus.Pending ? null : reviewedBy,
        ReviewedAt  = status == AbsenceStatus.Pending ? null : UtcAt(from.AddDays(-3), 10).UtcDateTime,
        CreatedAt   = UtcAt(from.AddDays(-7), 10).UtcDateTime,
    };

    /// <summary>Npgsql maps timestamptz strictly, so every instant must carry UTC kind.</summary>
    private static DateTimeOffset UtcAt(DateOnly day, int hour) =>
        new(DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(hour, 0)), DateTimeKind.Utc));

    private static string Slug(string value)
    {
        var chars = value.ToLowerInvariant()
            .Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o")
            .Replace("ú", "u").Replace("ý", "y").Replace("þ", "th").Replace("æ", "ae")
            .Replace("ö", "o").Replace("ð", "d")
            .Where(c => char.IsLetterOrDigit(c) || c == ' ')
            .ToArray();
        return new string(chars).Trim().Replace(" ", ".");
    }
}
