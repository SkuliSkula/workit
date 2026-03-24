using Microsoft.EntityFrameworkCore;
using Workit.Shared.Models;

namespace Workit.Api.Data;

/// <summary>
/// Seeds a full month of realistic test data for development.
/// Call via POST /api/dev/seed-test-data (dev only).
/// </summary>
public static class TestDataSeeder
{
    public static async Task<object> SeedFebruaryAsync(WorkitDbContext db, Guid companyId)
    {
        // ── 1. Ensure Company exists ─────────────────────────────────────────
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId);
        if (company is null)
            return new { Error = "Company not found" };

        // Update driving rate if not set
        if (company.DrivingUnitPrice == 0)
        {
            company.DrivingUnitPrice = 350m;
            company.StandardHoursPerDay = 8m;
        }

        // ── 2. Customers ─────────────────────────────────────────────────────
        var customers = new[]
        {
            new Customer { CompanyId = companyId, Name = "Reykjavík Properties ehf.", Ssn = "5012345679", Email = "info@rvkprop.is", Phone = "555-1010", ContactPerson = "Sigríður Jónsdóttir" },
            new Customer { CompanyId = companyId, Name = "Ístak hf.", Ssn = "6901234560", Email = "istak@istak.is", Phone = "555-2020", ContactPerson = "Bjarni Ólafsson" },
            new Customer { CompanyId = companyId, Name = "Borgarfjörður Supplies", Ssn = "4505678901", Email = "borgar@supplies.is", Phone = "555-3030", ContactPerson = "Helga Björnsdóttir" },
        };

        foreach (var c in customers)
        {
            if (!await db.Customers.AnyAsync(x => x.CompanyId == companyId && x.Name == c.Name))
                db.Customers.Add(c);
        }
        await db.SaveChangesAsync();

        // Reload to get IDs
        var allCustomers = await db.Customers.Where(c => c.CompanyId == companyId).ToListAsync();
        var cust1 = allCustomers.First(c => c.Name.Contains("Reykjavík"));
        var cust2 = allCustomers.First(c => c.Name.Contains("Ístak"));
        var cust3 = allCustomers.First(c => c.Name.Contains("Borgarfjörður"));

        // ── 3. Employees ─────────────────────────────────────────────────────
        var employeeDefs = new (string Name, string Trade, decimal Salary, decimal BillableRate)[]
        {
            ("Jón Sigurðsson",     "Rafvirki",      3200m, 5500m),
            ("Anna Björnsdóttir",  "Rafvirki",      3400m, 6000m),
            ("Magnús Þórsson",     "Pípulagningamaður", 3000m, 5000m),
            ("Katrín Helgadóttir", "Nemi",          2200m, 3500m),
        };

        foreach (var (name, trade, salary, rate) in employeeDefs)
        {
            if (!await db.Employees.AnyAsync(x => x.CompanyId == companyId && x.DisplayName == name))
            {
                db.Employees.Add(new Employee
                {
                    CompanyId = companyId,
                    DisplayName = name,
                    Trade = trade,
                    HourlySalary = salary,
                    HourlyBillableRate = rate,
                    EmploymentType = name.Contains("Nemi") ? EmploymentType.Contractor : EmploymentType.Employed,
                    Email = name.Split(' ')[0].ToLower() + "@workit.is",
                    Phone = "555-" + Random.Shared.Next(1000, 9999)
                });
            }
        }
        await db.SaveChangesAsync();

        var allEmployees = await db.Employees.Where(e => e.CompanyId == companyId).ToListAsync();
        var emp1 = allEmployees.First(e => e.DisplayName.Contains("Jón"));
        var emp2 = allEmployees.First(e => e.DisplayName.Contains("Anna"));
        var emp3 = allEmployees.First(e => e.DisplayName.Contains("Magnús"));
        var emp4 = allEmployees.First(e => e.DisplayName.Contains("Katrín"));

        // ── 4. Jobs ──────────────────────────────────────────────────────────
        var jobDefs = new (string Code, string Name, Guid CustomerId, BillingType Billing)[]
        {
            ("101", "Rafmagnsviðhald — Laugavegur 22",     cust1.Id, BillingType.Hourly),
            ("102", "Nýlagnir — Háaleitisbraut 68",        cust1.Id, BillingType.Hourly),
            ("201", "Verksmiðja endurbætur",                cust2.Id, BillingType.Hourly),
            ("202", "Skrifstofuhúsnæði — fast verð",        cust2.Id, BillingType.FixedPrice),
            ("301", "Pípulagnir — Borgarnes miðbær",       cust3.Id, BillingType.Hourly),
        };

        foreach (var (code, name, custId, billing) in jobDefs)
        {
            if (!await db.Jobs.AnyAsync(x => x.CompanyId == companyId && x.Code == code))
            {
                db.Jobs.Add(new Job
                {
                    CompanyId = companyId,
                    CustomerId = custId,
                    Code = code,
                    Name = name,
                    BillingType = billing
                });
            }
        }
        await db.SaveChangesAsync();

        var allJobs = await db.Jobs.Where(j => j.CompanyId == companyId).ToListAsync();
        var job101 = allJobs.First(j => j.Code == "101");
        var job102 = allJobs.First(j => j.Code == "102");
        var job201 = allJobs.First(j => j.Code == "201");
        var job202 = allJobs.First(j => j.Code == "202");
        var job301 = allJobs.First(j => j.Code == "301");

        // ── 5. Materials ─────────────────────────────────────────────────────
        var matDefs = new (string Code, string Name, string Category, string Unit, decimal Purchase, decimal Markup, decimal Vat, decimal Qty)[]
        {
            ("N1XE5G16",  "N1XE-U 5G 16 Cu 1kV Aflstrengur", "Rafstrengir", "m.",   890m, 1.5m, 24m, 500m),
            ("XHP2X5G10", "XHP2x 5G 10 Aflstrengur",         "Rafstrengir", "m.",   650m, 1.5m, 24m, 300m),
            ("LED24V50W", "LED Driver 24V 50W",               "Ljósabúnaður", "stk.", 4200m, 1.6m, 24m, 40m),
            ("CU15PIPE",  "Koparrör 15mm",                    "Pípulagnir",  "m.",   420m, 1.5m, 24m, 200m),
            ("ELBOW15",   "Kopar olnbogi 15mm",               "Pípulagnir",  "stk.", 180m, 1.5m, 24m, 100m),
            ("SWITCH1G",  "Rofi 1-pól",                       "Rofar og tenglar", "stk.", 950m, 1.6m, 24m, 80m),
            ("SOCKET230", "Innstunga 230V 16A",               "Rofar og tenglar", "stk.", 780m, 1.5m, 24m, 60m),
        };

        foreach (var (code, name, cat, unit, purchase, markup, vat, qty) in matDefs)
        {
            if (!await db.Materials.AnyAsync(x => x.CompanyId == companyId && x.ProductCode == code))
            {
                db.Materials.Add(new Material
                {
                    CompanyId = companyId,
                    ProductCode = code,
                    Name = name,
                    Category = cat,
                    Unit = unit,
                    PurchasePrice = purchase,
                    MarkupFactor = markup,
                    UnitPrice = Math.Round(purchase * markup, 0),
                    VatRate = vat,
                    Quantity = qty,
                    IsActive = true
                });
            }
        }
        await db.SaveChangesAsync();

        var allMaterials = await db.Materials.Where(m => m.CompanyId == companyId).ToListAsync();

        // ── 6. Time Entries for February 2026 ────────────────────────────────
        // February 2026: 1st is Sunday, 28 days. Working days: 2-6, 9-13, 16-20, 23-27 = 20 days
        var feb2026 = new DateOnly(2026, 2, 1);
        var existingFeb = await db.TimeEntries
            .AnyAsync(x => x.CompanyId == companyId && x.WorkDate >= feb2026 && x.WorkDate < feb2026.AddMonths(1));

        int timeEntriesCreated = 0;
        int materialUsagesCreated = 0;

        if (!existingFeb)
        {
            // Work schedule: Each employee works different jobs on different days
            // Realistic pattern: some days with overtime, some with driving
            var schedule = new List<(Employee Emp, Job Job, DateOnly Date, decimal Hours, decimal OT, int Driving, string Notes)>();

            for (var day = feb2026; day.Month == 2; day = day.AddDays(1))
            {
                // Skip weekends
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    continue;

                var dayNum = day.Day;

                // Jón: works on job 101 and 102 for customer 1
                if (dayNum <= 14)
                {
                    schedule.Add((emp1, job101, day, 8m, dayNum % 3 == 0 ? 1.5m : 0m, dayNum % 4 == 0 ? 2 : 0,
                        dayNum % 3 == 0 ? "Viðgerð á aðaltöflu" : "Lagnir í kjallara"));
                }
                else
                {
                    schedule.Add((emp1, job102, day, 8m, dayNum % 5 == 0 ? 2m : 0m, dayNum % 3 == 0 ? 3 : 0,
                        "Nýlagnir — 2. hæð"));
                }

                // Anna: works on job 201 (factory) full month
                schedule.Add((emp2, job201, day, 8m, dayNum % 4 == 0 ? 2m : 0m, dayNum % 5 == 0 ? 1 : 0,
                    dayNum % 2 == 0 ? "Stýritöflur" : "Lýsingakerfi"));

                // Magnús: works on job 301 (plumbing) and some days on 202
                if (dayNum % 5 != 0) // 4 out of 5 days on plumbing
                {
                    schedule.Add((emp3, job301, day, 8m, dayNum % 6 == 0 ? 1m : 0m, dayNum % 3 == 0 ? 4 : 0,
                        "Pípulagnir — verslunarrými"));
                }
                else // every 5th day on the fixed-price office job
                {
                    schedule.Add((emp3, job202, day, 6m, 0m, 1,
                        "Pípulagnaútköll"));
                }

                // Katrín (apprentice): half-days on 101, full days on 201
                if (dayNum <= 14)
                {
                    schedule.Add((emp4, job101, day, 4m, 0m, 0, "Aðstoð við lagnir"));
                    schedule.Add((emp4, job201, day, 4m, 0m, 0, "Aðstoð — merking"));
                }
                else
                {
                    schedule.Add((emp4, job102, day, 8m, 0m, dayNum % 4 == 0 ? 1 : 0, "Aðstoð við nýlagnir"));
                }
            }

            foreach (var (emp, job, date, hours, ot, driving, notes) in schedule)
            {
                db.TimeEntries.Add(new TimeEntry
                {
                    CompanyId = companyId,
                    EmployeeId = emp.Id,
                    JobId = job.Id,
                    WorkDate = date,
                    Hours = hours,
                    OvertimeHours = ot,
                    DrivingUnits = driving,
                    Notes = notes
                });
                timeEntriesCreated++;
            }

            await db.SaveChangesAsync();

            // ── 7. Material Usage for February 2026 ──────────────────────────
            var matByCode = allMaterials.ToDictionary(m => m.ProductCode);

            var usageDefs = new (string MatCode, Employee Emp, Job Job, decimal Qty, int DayOffset, string Notes)[]
            {
                // Job 101 — Laugavegur electrical maintenance
                ("N1XE5G16",  emp1, job101,  25m,  2, "Aðalstrengur — kjallari"),
                ("SWITCH1G",  emp1, job101,   6m,  5, "Rofar — 1. hæð"),
                ("SOCKET230", emp1, job101,   8m,  8, "Innstungur — 1. og 2. hæð"),
                ("LED24V50W", emp1, job101,   4m, 10, "LED driverar í gangi"),

                // Job 102 — Háaleitisbraut new installation
                ("N1XE5G16",  emp1, job102,  80m, 17, "Lagnir — 2. hæð"),
                ("XHP2X5G10", emp1, job102,  45m, 19, "Lagnir — 3. hæð"),
                ("SWITCH1G",  emp1, job102,  12m, 20, "Rofar — allar hæðir"),
                ("SOCKET230", emp1, job102,  16m, 23, "Innstungur — allar hæðir"),
                ("LED24V50W", emp4, job102,   8m, 24, "LED driverar — ný lýsing"),

                // Job 201 — Factory renovation
                ("XHP2X5G10", emp2, job201,  60m,  3, "Stýritöflulagnir"),
                ("N1XE5G16",  emp2, job201,  40m,  9, "Aðalstrengur að nýrri töflu"),
                ("LED24V50W", emp2, job201,  12m, 12, "LED driverar — verksmiðjuhús"),
                ("SWITCH1G",  emp2, job201,   8m, 16, "Stýrirofar"),

                // Job 301 — Borgarnes plumbing
                ("CU15PIPE",  emp3, job301,  35m,  4, "Lagnir — verslunarrými"),
                ("ELBOW15",   emp3, job301,  18m,  6, "Olnbogar og tengi"),
                ("CU15PIPE",  emp3, job301,  28m, 18, "Lagnir — baðherbergi"),
                ("ELBOW15",   emp3, job301,  12m, 22, "Tengi — baðherbergi"),

                // Job 202 (fixed price) — some plumbing materials
                ("CU15PIPE",  emp3, job202,  10m, 15, "Pípuútköll"),
                ("ELBOW15",   emp3, job202,   4m, 15, "Tengi"),
            };

            foreach (var (matCode, emp, job, qty, dayOffset, notes) in usageDefs)
            {
                if (!matByCode.TryGetValue(matCode, out var mat)) continue;

                var usedAt = new DateTimeOffset(2026, 2, Math.Min(dayOffset, 28), 10, 0, 0, TimeSpan.Zero);

                db.MaterialUsages.Add(new MaterialUsage
                {
                    CompanyId = companyId,
                    MaterialId = mat.Id,
                    EmployeeId = emp.Id,
                    JobId = job.Id,
                    Quantity = qty,
                    UsedAt = usedAt,
                    Notes = notes
                });

                // Deduct from stock (same as the normal POST endpoint)
                mat.Quantity = Math.Max(0, mat.Quantity - qty);
                materialUsagesCreated++;
            }

            await db.SaveChangesAsync();
        }

        // ── Summary ──────────────────────────────────────────────────────────
        var totalEntries = await db.TimeEntries.CountAsync(x => x.CompanyId == companyId && x.WorkDate >= feb2026 && x.WorkDate < feb2026.AddMonths(1));
        var totalUsages = await db.MaterialUsages.CountAsync(x => x.CompanyId == companyId && x.UsedAt >= new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero) && x.UsedAt < new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var totalHours = await db.TimeEntries.Where(x => x.CompanyId == companyId && x.WorkDate >= feb2026 && x.WorkDate < feb2026.AddMonths(1)).SumAsync(x => x.Hours);
        var totalOt = await db.TimeEntries.Where(x => x.CompanyId == companyId && x.WorkDate >= feb2026 && x.WorkDate < feb2026.AddMonths(1)).SumAsync(x => x.OvertimeHours);

        return new
        {
            Message = existingFeb ? "February data already exists — skipped time entries and material usage" : "Test data seeded successfully",
            Customers = allCustomers.Count,
            Employees = allEmployees.Count,
            Jobs = allJobs.Count,
            Materials = allMaterials.Count,
            February = new
            {
                TimeEntries = totalEntries,
                NewTimeEntries = timeEntriesCreated,
                MaterialUsages = totalUsages,
                NewMaterialUsages = materialUsagesCreated,
                TotalHours = totalHours,
                TotalOvertimeHours = totalOt
            }
        };
    }
}
