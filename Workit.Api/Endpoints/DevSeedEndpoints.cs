using Microsoft.EntityFrameworkCore;
using Workit.Api.Auth;
using Workit.Api.Data;
using Workit.Shared.Auth;
using Workit.Shared.Models;

namespace Workit.Api.Endpoints;

public static class DevSeedEndpoints
{
    public static void MapDevSeedEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;

        app.MapPost("/api/dev/seed", async (WorkitDbContext db, CancellationToken ct) =>
        {
            if (await db.AppUsers.AnyAsync(u => u.Role == WorkitRoles.Owner, ct))
                return Results.BadRequest("Seed data already exists. Clear the database first.");

            const string password = "Test1234!";
            var rng = new Random(42);

            // ── Companies ─────────────────────────────────────────────────────
            var compA = new Company { Id = Guid.NewGuid(), Name = "Rafvirki ehf.",   Ssn = "5501012340", Email = "rafvirki@test.is",   Phone = "5551000", Address = "Ármúli 1, 108 Reykjavík",           Owner = "Jón Sigurðsson",    DrivingUnitPrice = 120, StandardHoursPerDay = 8 };
            var compB = new Company { Id = Guid.NewGuid(), Name = "Pípulagning hf.", Ssn = "6601023450", Email = "pipu@test.is",        Phone = "5552000", Address = "Borgartún 5, 105 Reykjavík",         Owner = "Jón Sigurðsson",    DrivingUnitPrice = 120, StandardHoursPerDay = 8 };
            var compC = new Company { Id = Guid.NewGuid(), Name = "Múrverk sf.",     Ssn = "7701034560", Email = "murverk@test.is",     Phone = "5553000", Address = "Suðurlandsbraut 12, 108 Reykjavík", Owner = "María Ólafsdóttir", DrivingUnitPrice = 100, StandardHoursPerDay = 8 };
            db.Companies.AddRange(compA, compB, compC);

            // ── Owners ────────────────────────────────────────────────────────
            var owner1 = new AppUser { Id = Guid.NewGuid(), Name = "Jón Sigurðsson",    Email = "jon@test.is",   PasswordHash = PasswordHasher.HashPassword(password), Role = WorkitRoles.Owner, CompanyId = compA.Id };
            var owner2 = new AppUser { Id = Guid.NewGuid(), Name = "María Ólafsdóttir", Email = "maria@test.is", PasswordHash = PasswordHasher.HashPassword(password), Role = WorkitRoles.Owner, CompanyId = compC.Id };
            db.AppUsers.AddRange(owner1, owner2);

            db.UserCompanies.AddRange(
                new UserCompany { UserId = owner1.Id, CompanyId = compA.Id },
                new UserCompany { UserId = owner1.Id, CompanyId = compB.Id },
                new UserCompany { UserId = owner2.Id, CompanyId = compC.Id }
            );

            // ── Customers ─────────────────────────────────────────────────────
            var custA = CreateCustomers(compA.Id);
            var custB = CreateCustomers(compB.Id);
            var custC = CreateCustomers(compC.Id);
            db.Customers.AddRange([.. custA, .. custB, .. custC]);

            // ── Employees + AppUsers ───────────────────────────────────────────
            var (empsA, usersA) = CreateEmployees(compA.Id, password, "Rafvirki ehf.",   rng);
            var (empsB, usersB) = CreateEmployees(compB.Id, password, "Pípulagning hf.", rng);
            var (empsC, usersC) = CreateEmployees(compC.Id, password, "Múrverk sf.",     rng);
            db.Employees.AddRange([.. empsA, .. empsB, .. empsC]);
            db.AppUsers.AddRange([.. usersA, .. usersB, .. usersC]);

            // ── Tools ─────────────────────────────────────────────────────────
            db.Tools.AddRange([
                .. CreateTools(compA.Id, "Rafvirki ehf."),
                .. CreateTools(compB.Id, "Pípulagning hf."),
                .. CreateTools(compC.Id, "Múrverk sf."),
            ]);

            // ── Materials ─────────────────────────────────────────────────────
            var matsA = CreateMaterials(compA.Id, "electrical");
            var matsB = CreateMaterials(compB.Id, "plumbing");
            var matsC = CreateMaterials(compC.Id, "masonry");
            db.Materials.AddRange([.. matsA, .. matsB, .. matsC]);

            await db.SaveChangesAsync(ct);

            // ── Jobs (with kanban states) ──────────────────────────────────────
            var jobsA = CreateJobs(compA.Id, custA, "electrical");
            var jobsB = CreateJobs(compB.Id, custB, "plumbing");
            var jobsC = CreateJobs(compC.Id, custC, "masonry");
            db.Jobs.AddRange([.. jobsA, .. jobsB, .. jobsC]);

            await db.SaveChangesAsync(ct);

            // ── Time Entries (full year 2025) ──────────────────────────────────
            var entries = new List<TimeEntry>();
            entries.AddRange(CreateYearEntries(compA.Id, empsA, jobsA, rng));
            entries.AddRange(CreateYearEntries(compB.Id, empsB, jobsB, rng));
            entries.AddRange(CreateYearEntries(compC.Id, empsC, jobsC, rng));
            db.TimeEntries.AddRange(entries);

            // ── Absence Requests ──────────────────────────────────────────────
            var absences = new List<AbsenceRequest>();
            absences.AddRange(CreateAbsences(compA.Id, empsA, owner1.Id, rng));
            absences.AddRange(CreateAbsences(compB.Id, empsB, owner1.Id, rng));
            absences.AddRange(CreateAbsences(compC.Id, empsC, owner2.Id, rng));
            db.AbsenceRequests.AddRange(absences);

            // ── Material Usages ────────────────────────────────────────────────
            var usages = new List<MaterialUsage>();
            usages.AddRange(CreateMaterialUsages(compA.Id, empsA, jobsA, matsA, rng));
            usages.AddRange(CreateMaterialUsages(compB.Id, empsB, jobsB, matsB, rng));
            usages.AddRange(CreateMaterialUsages(compC.Id, empsC, jobsC, matsC, rng));
            db.MaterialUsages.AddRange(usages);

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                companies    = 3,
                owners       = 2,
                customers    = custA.Count + custB.Count + custC.Count,
                jobs         = jobsA.Count + jobsB.Count + jobsC.Count,
                employees    = empsA.Count + empsB.Count + empsC.Count,
                materials    = matsA.Count + matsB.Count + matsC.Count,
                timeEntries  = entries.Count,
                absences     = absences.Count,
                materialUsages = usages.Count,
                credentials  = new { email1 = "jon@test.is", email2 = "maria@test.is", password }
            });
        });
    }

    // ── Customers ─────────────────────────────────────────────────────────────

    private static List<Customer> CreateCustomers(Guid companyId) =>
    [
        new() { CompanyId = companyId, Name = "Bygg ehf.",              Ssn = "4401012340", Email = "bygg@test.is",         Phone = "4441000", ContactPerson = "Anna Björnsdóttir"      },
        new() { CompanyId = companyId, Name = "Íbúðafélagið hf.",       Ssn = "5502024560", Email = "ibuda@test.is",         Phone = "4442000", ContactPerson = "Gunnar Pétursson"       },
        new() { CompanyId = companyId, Name = "Storkur sf.",             Ssn = "6603036780", Email = "storkur@test.is",       Phone = "4443000", ContactPerson = "Sigríður Magnúsdóttir"  },
        new() { CompanyId = companyId, Name = "Norðurljós ehf.",         Ssn = "7704048900", Email = "nordurljos@test.is",    Phone = "4444000", ContactPerson = "Einar Stefánsson"       },
        new() { CompanyId = companyId, Name = "Húsasmiðjan hf.",         Ssn = "4805051230", Email = "husasmidjan@test.is",   Phone = "4445000", ContactPerson = "Þóra Kristjánsdóttir"  },
        new() { CompanyId = companyId, Name = "Verzló eignir ehf.",      Ssn = "5906062340", Email = "verzlo@test.is",        Phone = "4446000", ContactPerson = "Árni Björnsson"         },
    ];

    // ── Employees ─────────────────────────────────────────────────────────────

    private static (List<Employee> employees, List<AppUser> users) CreateEmployees(
        Guid companyId, string password, string companyName, Random rng)
    {
        string[] trades     = ["Rafvirki", "Pípulagningamaður", "Múrari", "Trésmíðamaður", "Málari"];
        string[] firstNames = ["Ólafur", "Guðmundur", "Sigurður", "Jón", "Gunnar", "Kristján", "Bjarni", "Helgi", "Magnús"];
        string[] lastNames  = ["Jónsson", "Sigurðsson", "Gunnarsson", "Björnsson", "Pétursson", "Ólafsson", "Einarsson", "Stefánsson", "Kristjánsson"];

        var employees = new List<Employee>();
        var users     = new List<AppUser>();
        var slug      = companyName.Split(' ')[0].ToLowerInvariant();

        for (var i = 0; i < 9; i++)
        {
            var fn    = firstNames[i];
            var ln    = lastNames[i];
            var trade = trades[i % trades.Length];
            var emp   = new Employee
            {
                Id                 = Guid.NewGuid(),
                CompanyId          = companyId,
                DisplayName        = $"{fn} {ln}",
                Trade              = trade,
                Ssn                = $"{2001 + i:D4}01{i + 1:D2}490",
                Email              = $"{fn.ToLowerInvariant()}.{ln.ToLowerInvariant()}@{slug}.is",
                Phone              = $"77{i:D2}000",
                HourlySalary       = 2800 + rng.Next(0, 800),
                HourlyBillableRate = 5000 + rng.Next(0, 2000),
                EmploymentType     = i < 7 ? EmploymentType.Employed : EmploymentType.Contractor,
            };
            var user = new AppUser
            {
                CompanyId    = companyId,
                EmployeeId   = emp.Id,
                Name         = emp.DisplayName,
                Email        = emp.Email,
                PasswordHash = PasswordHasher.HashPassword(password),
                Role         = WorkitRoles.Employee,
            };
            employees.Add(emp);
            users.Add(user);
        }

        return (employees, users);
    }

    // ── Tools ─────────────────────────────────────────────────────────────────

    private static List<Tool> CreateTools(Guid companyId, string companyName)
    {
        string[] electricalTools = ["Dremel 4300", "Makita DHP484 borvél", "Fluke 117 rafmagnsmælir", "Klein Tools snyrting", "Milwaukee M12 ljós", "Bosch GSB 18V-55 borvél", "Hilti TE 2-A22 hamar", "Fluke 376 straumtang", "Klauke ES 32 þjöppunartól", "Leica D2 laserregel"];
        string[] plumbingTools   = ["Rothenberger ROMAX borvél", "Ridgid 258 þráðfléttur", "Virax 232610 þrýstipróf", "Rothenberger Rotest GE prufa", "Hilti DX 460 þrýstibyssu", "Reed VPCL frostlykill", "Rems Eco-Press þjöppunartól", "Milwaukee M18 þrykkjupumpa", "Bosch GAS 25 L SFC ryksuga", "Fluke 922 loftþrýstimælir"];
        string[] masonryTools    = ["Hilti TE 70-ATC/AVR hamar", "Makita HM1812X3 lofthamar", "Bosch GBH 5-40 DE hamar", "Dewalt D25333K hamar", "Husqvarna DM 220 kjarnaborar", "Stihl TS 410 skurðartól", "Rubi DC-250 1200 flísasög", "Bosch GKS 85 G sirkelsög", "Festool TS 75 EBQ sög", "Leica Rugby 640 laserregel"];

        var names = companyName.Contains("Raf") ? electricalTools
                  : companyName.Contains("Pípu") ? plumbingTools
                  : masonryTools;

        return names.Select((name, i) => new Tool
        {
            CompanyId    = companyId,
            Name         = name,
            Description  = $"Tól númer {i + 1} hjá {companyName}",
            SerialNumber = $"SN-{companyId.ToString()[..4].ToUpper()}-{i + 1:D3}",
        }).ToList();
    }

    // ── Materials ─────────────────────────────────────────────────────────────

    private static List<Material> CreateMaterials(Guid companyId, string trade)
    {
        var items = trade switch
        {
            "electrical" => new[]
            {
                ("N1XE-U 5G 1.5 Cu 1kV leiðari",      "EL-001", "Leiðarar",    "m.",    500m,  420m,  630m),
                ("N1XE-U 5G 2.5 Cu 1kV leiðari",      "EL-002", "Leiðarar",    "m.",    800m,  580m,  870m),
                ("N1XE-U 5G 6 Cu 1kV leiðari",        "EL-003", "Leiðarar",    "m.",    300m,  920m, 1380m),
                ("NYM-J 3x1.5 leiðari",                "EL-004", "Leiðarar",    "m.",    600m,  290m,  435m),
                ("NYM-J 3x2.5 leiðari",                "EL-005", "Leiðarar",    "m.",    400m,  390m,  585m),
                ("Schneider iC60 16A B-rof",           "EL-010", "Verndarar",   "stk.",  200m,  650m,  975m),
                ("Schneider iC60 25A C-rof",           "EL-011", "Verndarar",   "stk.",  150m,  780m, 1170m),
                ("Schneider iC60 32A C-rof",           "EL-012", "Verndarar",   "stk.",  100m,  890m, 1335m),
                ("ABB F204 30mA jarðtenglavörn",       "EL-013", "Verndarar",   "stk.",   80m, 3200m, 4800m),
                ("Hager MGB116A mælaborð 16-eininga",  "EL-020", "Mælaborð",    "stk.",   10m,28000m,42000m),
                ("Legrand 774160 tjakkdós",             "EL-030", "Dósir",       "stk.",  500m,  180m,  270m),
                ("Legrand 774162 útfallsdós",           "EL-031", "Dósir",       "stk.",  400m,  220m,  330m),
                ("Schneider Odace ljósrofi",            "EL-040", "Rofahlutir",  "stk.",  300m,  520m,  780m),
                ("Schneider Odace 16A innstunga",       "EL-041", "Rofahlutir",  "stk.",  350m,  680m, 1020m),
                ("Philips CorePro LED 9W E27",          "EL-050", "Lýsing",      "stk.",  200m,  890m, 1335m),
                ("Philips CorePro LED 15W E27",         "EL-051", "Lýsing",      "stk.",  100m, 1150m, 1725m),
                ("Osram Ledvance panel 60x60 36W",      "EL-052", "Lýsing",      "stk.",   50m, 7800m,11700m),
                ("DIN listi 35mm 1m",                   "EL-060", "Fylgihlutir", "stk.",  100m,  480m,  720m),
                ("Kabelskór 2.5mm² blár 100 stk",       "EL-061", "Fylgihlutir", "pk.",   200m,  690m, 1035m),
                ("Varaplástur PVC 20mm grár 50m",       "EL-062", "Fylgihlutir", "rúll.",  80m, 1200m, 1800m),
            },
            "plumbing" => new[]
            {
                ("Uponor 16mm PE-Xa lagnir 100m",      "PL-001", "Lagnir",       "rúll.",  20m,18500m,27750m),
                ("Uponor 20mm PE-Xa lagnir 50m",       "PL-002", "Lagnir",       "rúll.",  15m,14200m,21300m),
                ("Uponor 25mm PE-Xa lagnir 25m",       "PL-003", "Lagnir",       "rúll.",  10m,12800m,19200m),
                ("Copper 15mm þykkt 3m",               "PL-004", "Lagnir",       "stk.",  200m, 2200m, 3300m),
                ("Copper 22mm þykkt 3m",               "PL-005", "Lagnir",       "stk.",  100m, 3800m, 5700m),
                ("Uponor Q&E 16mm bogasamskeyti",      "PL-010", "Samskeyti",    "stk.",  500m,  620m,  930m),
                ("Uponor Q&E 20mm bogasamskeyti",      "PL-011", "Samskeyti",    "stk.",  300m,  820m, 1230m),
                ("Uponor Q&E 16x1/2 veggsamskeyti",   "PL-012", "Samskeyti",    "stk.",  200m,  980m, 1470m),
                ("Geberit Duofix WC rammi",            "PL-020", "Salernistæki", "stk.",   20m,42000m,63000m),
                ("Geberit Sigma20 þvottaklafi",        "PL-021", "Salernistæki", "stk.",   25m,18500m,27750m),
                ("Grohe Eurosmart blöndunarr 35mm",    "PL-030", "Blöndunartæki","stk.",   30m,28000m,42000m),
                ("Grohe Eurosmart sturtuhaus",         "PL-031", "Blöndunartæki","stk.",   20m,32000m,48000m),
                ("Ballofix 15mm lokavatn",             "PL-040", "Lokar",        "stk.",  100m, 2800m, 4200m),
                ("Ballofix 22mm lokavatn",             "PL-041", "Lokar",        "stk.",   80m, 4200m, 6300m),
                ("Fernox F1 kvarðaefni 500ml",         "PL-050", "Kvarðaefni",   "stk.",   50m, 3200m, 4800m),
                ("Sievert blásari kit 3485",           "PL-051", "Kvarðaefni",   "stk.",   10m,18000m,27000m),
                ("Rothenberger þræðaolía 1L",          "PL-052", "Kvarðaefni",   "stk.",   30m, 1800m, 2700m),
                ("Mepla 16mm PE-Xc 5m",               "PL-060", "Lagnir",        "stk.",  100m, 4200m, 6300m),
                ("Isover Rörskål 22/30mm 1m",          "PL-070", "Einangrun",    "stk.",  200m,  980m, 1470m),
                ("Isover Rörskål 28/30mm 1m",          "PL-071", "Einangrun",    "stk.",  150m, 1250m, 1875m),
            },
            _ => new[] // masonry
            {
                ("Portland sement CEM I 42.5 25kg",   "MU-001", "Sement",      "stk.",  400m, 1800m, 2700m),
                ("Portland sement CEM II 32.5 25kg",  "MU-002", "Sement",      "stk.",  200m, 1600m, 2400m),
                ("Múrblanda M5 25kg",                  "MU-003", "Múrblanda",   "stk.",  500m, 1200m, 1800m),
                ("Múrblanda M10 25kg",                 "MU-004", "Múrblanda",   "stk.",  300m, 1400m, 2100m),
                ("Porobeton B6 600x300x200",           "MU-010", "Steinn",      "stk.",  500m,  980m, 1470m),
                ("Porobeton B4 600x300x150",           "MU-011", "Steinn",      "stk.",  400m,  780m, 1170m),
                ("Leca blokk 40x20x20 standard",      "MU-012", "Steinn",      "stk.",  800m,  620m,  930m),
                ("Leca blokk 40x20x15 þunnt",         "MU-013", "Steinn",      "stk.",  600m,  520m,  780m),
                ("Múrkamb gr. 50 3m",                  "MU-020", "Búnaður",     "stk.",  100m, 2800m, 4200m),
                ("Hilti HIT-RE 500 v3 lím 330ml",      "MU-021", "Festar",      "stk.",   80m, 6800m,10200m),
                ("Hilti HST3-R M10x90 festibolti",     "MU-022", "Festar",      "pk.",   200m, 4200m, 6300m),
                ("Fischer FIS V 360 S festar",         "MU-023", "Festar",      "stk.",   60m, 7200m,10800m),
                ("Mapei Keraflex maxi S1 25kg",        "MU-030", "Flísalím",    "stk.",   80m, 4800m, 7200m),
                ("Mapei Ultracolor plus FA 5kg",       "MU-031", "Flísalím",    "stk.",  100m, 3200m, 4800m),
                ("Rockwool Flexi einangrunarplata",    "MU-040", "Einangrun",   "stk.",  200m, 3800m, 5700m),
                ("Isover Glasswool 100mm 6m²",        "MU-041", "Einangrun",   "stk.",  150m, 4200m, 6300m),
                ("Bramac Express taksteinn",           "MU-050", "Þak",         "stk.",  300m, 1200m, 1800m),
                ("Icopal bitumenvara 10m²",            "MU-051", "Þak",         "rúll.",  40m,12000m,18000m),
                ("Gyproc GN13 gipsplata 2.6m",        "MU-060", "Gips",        "stk.",  250m, 2800m, 4200m),
                ("Gyproc GEK13 eldfastplata 2.6m",    "MU-061", "Gips",        "stk.",  100m, 3400m, 5100m),
            }
        };

        return items.Select(x => new Material
        {
            CompanyId     = companyId,
            Name          = x.Item1,
            ProductCode   = x.Item2,
            Category      = x.Item3,
            Unit          = x.Item4,
            Quantity      = x.Item5,
            PurchasePrice = x.Item6,
            UnitPrice     = x.Item7,
            MarkupFactor  = Math.Round(x.Item7 / x.Item6, 2),
            VatRate       = 24.0m,
            IsActive      = true,
        }).ToList();
    }

    // ── Jobs ──────────────────────────────────────────────────────────────────
    // Lane rules (from Kanban.razor):
    //   Backlog    — no time entries
    //   InProgress — has entries, not all invoiced, KanbanStatus = Active
    //   Waiting    — has entries, not all invoiced, KanbanStatus = Waiting
    //   Done       — all entries IsInvoiced = true  (+KanbanDoneAt set)

    private record JobDef(
        string Code, string Name,
        JobCategory Category, BillingType Billing,
        SeedLane Lane,
        DateOnly StartDate, DateOnly EndDate,
        string? WaitingReason,
        int[] EmpSlots,   // indices into the 9-employee array
        int CustomerIdx);

    private enum SeedLane { Backlog, InProgress, Waiting, Done }

    private static List<Job> CreateJobs(Guid companyId, List<Customer> customers, string trade)
    {
        JobDef[] defs = trade switch
        {
            "electrical" =>
            [
                // ── Done (5) ──────────────────────────────────────────────────
                new("D-001", "Laugavegur 22 — innlagnir",             JobCategory.NewInstallation, BillingType.Hourly,     SeedLane.Done,       new(2025,1,6),  new(2025,3,28), null, [0,1,3], 0),
                new("D-002", "Breiðholtsskóli — viðhald rafkerfis",   JobCategory.Maintenance,     BillingType.Hourly,     SeedLane.Done,       new(2025,2,3),  new(2025,4,30), null, [0,2],   1),
                new("D-003", "Kringlan — aðalborð endurnýjun",        JobCategory.Repair,          BillingType.Hourly,     SeedLane.Done,       new(2025,3,3),  new(2025,5,30), null, [1,3],   2),
                new("D-004", "Smárabíó — neyðarlýsing",               JobCategory.NewInstallation, BillingType.FixedPrice, SeedLane.Done,       new(2025,4,1),  new(2025,6,27), null, [0,4],   3),
                new("D-005", "BSÍ — öryggisvísar og neyðarljós",      JobCategory.Inspection,      BillingType.Hourly,     SeedLane.Done,       new(2025,5,5),  new(2025,7,31), null, [2,3],   4),
                // ── InProgress (4) ────────────────────────────────────────────
                new("P-001", "Borgartún 5 — þriggja hæða lagnir",     JobCategory.NewInstallation, BillingType.Hourly,     SeedLane.InProgress, new(2025,7,1),  new(2025,12,31), null, [0,1,2], 0),
                new("P-002", "Grafarvogur — íbúðaviðhald",             JobCategory.Maintenance,     BillingType.Hourly,     SeedLane.InProgress, new(2025,8,4),  new(2025,12,31), null, [3,4],   1),
                new("P-003", "Háskóli Íslands — LED uppfærsla",        JobCategory.NewInstallation, BillingType.FixedPrice, SeedLane.InProgress, new(2025,9,1),  new(2025,12,31), null, [0,5],   2),
                new("P-004", "Smárinn — ársviðhald rafkerfis",         JobCategory.Maintenance,     BillingType.Hourly,     SeedLane.InProgress, new(2025,10,1), new(2025,12,31), null, [6,7],   5),
                // ── Waiting (2) ───────────────────────────────────────────────
                new("W-001", "Árbær — nýbygging rafkerfi",             JobCategory.NewInstallation, BillingType.Hourly,     SeedLane.Waiting,    new(2025,5,5),  new(2025,9,12), "Beðið eftir byggingarleyfi frá Reykjavíkurborg", [1,2], 3),
                new("W-002", "Sundlaug Laugardalur — endurnýjun",      JobCategory.Repair,          BillingType.FixedPrice, SeedLane.Waiting,    new(2025,6,2),  new(2025,10,10), "Beðið eftir búnaði frá Þýskalandi", [0,3], 4),
                // ── Backlog (3) ───────────────────────────────────────────────
                new("B-001", "Seltjarnarnes — ný íbúðarbyggð",         JobCategory.NewInstallation, BillingType.Hourly,     SeedLane.Backlog,    new(2026,1,1),  new(2026,1,1),  null, [], 0),
                new("B-002", "Akureyri útibú — ársviðhald",            JobCategory.Maintenance,     BillingType.Hourly,     SeedLane.Backlog,    new(2026,1,1),  new(2026,1,1),  null, [], 1),
                new("B-003", "Hótel Örk — eftirlitsferð",              JobCategory.Inspection,      BillingType.Hourly,     SeedLane.Backlog,    new(2026,1,1),  new(2026,1,1),  null, [], 2),
            ],
            "plumbing" =>
            [
                // ── Done (5) ──────────────────────────────────────────────────
                new("D-001", "Reykjanes — hitaveitutenging",           JobCategory.NewInstallation, BillingType.Hourly,     SeedLane.Done,       new(2025,1,6),  new(2025,3,31), null, [0,1],   0),
                new("D-002", "Ármúli — baðherbergi 12 íbúðir",        JobCategory.Repair,          BillingType.Hourly,     SeedLane.Done,       new(2025,2,3),  new(2025,4,30), null, [2,3],   1),
                new("D-003", "Kópavogur — kælikerfi skrifstofa",      JobCategory.NewInstallation, BillingType.FixedPrice, SeedLane.Done,       new(2025,3,3),  new(2025,5,30), null, [0,4],   2),
                new("D-004", "Hafnarfjörður — pípuviðhald",            JobCategory.Maintenance,     BillingType.Hourly,     SeedLane.Done,       new(2025,4,7),  new(2025,6,27), null, [1,2],   3),
                new("D-005", "Garðabær — nýbygging pípulagnir",        JobCategory.NewInstallation, BillingType.Hourly,     SeedLane.Done,       new(2025,5,5),  new(2025,8,29), null, [0,3,4], 4),
                // ── InProgress (4) ────────────────────────────────────────────
                new("P-001", "Mosfellsbær — hitaveita tenging",        JobCategory.NewInstallation, BillingType.Hourly,     SeedLane.InProgress, new(2025,7,1),  new(2025,12,31), null, [0,1,2], 0),
                new("P-002", "Suðurnes — vatnsveituuppfærsla",         JobCategory.Repair,          BillingType.Hourly,     SeedLane.InProgress, new(2025,8,4),  new(2025,12,31), null, [3,5],   1),
                new("P-003", "Selfoss — nýtt þvottahús",               JobCategory.NewInstallation, BillingType.FixedPrice, SeedLane.InProgress, new(2025,9,1),  new(2025,12,31), null, [0,4],   2),
                new("P-004", "Vesturbær — gömul lögn skipt út",        JobCategory.Repair,          BillingType.Hourly,     SeedLane.InProgress, new(2025,10,6), new(2025,12,31), null, [6,7],   5),
                // ── Waiting (2) ───────────────────────────────────────────────
                new("W-001", "Álftanes — geothermísk könnun",          JobCategory.Consultation,    BillingType.Hourly,     SeedLane.Waiting,    new(2025,6,2),  new(2025,9,19), "Beðið eftir lóðaréttindum", [1,3], 3),
                new("W-002", "Norðlingaholt — hitaveita stækkun",      JobCategory.NewInstallation, BillingType.Hourly,     SeedLane.Waiting,    new(2025,7,7),  new(2025,10,17), "Beðið eftir samþykki frá Orkuveitu", [0,2], 4),
                // ── Backlog (3) ───────────────────────────────────────────────
                new("B-001", "Breiðholt — íbúðabyggð pípulagnir",      JobCategory.NewInstallation, BillingType.Hourly,     SeedLane.Backlog,    new(2026,1,1),  new(2026,1,1),  null, [], 0),
                new("B-002", "Þórshöfn — vatnsveita endurnýjun",       JobCategory.Repair,          BillingType.Hourly,     SeedLane.Backlog,    new(2026,1,1),  new(2026,1,1),  null, [], 1),
                new("B-003", "Ísafjörður — hitaveitutenging",          JobCategory.NewInstallation, BillingType.Hourly,     SeedLane.Backlog,    new(2026,1,1),  new(2026,1,1),  null, [], 2),
            ],
            _ => // masonry
            [
                // ── Done (5) ──────────────────────────────────────────────────
                new("D-001", "Reykjavík — múrviðhald utanhúss",        JobCategory.Repair,          BillingType.Hourly,     SeedLane.Done,       new(2025,1,6),  new(2025,4,30), null, [0,1,2], 0),
                new("D-002", "Akranes — steypuendurbætur brú",         JobCategory.Repair,          BillingType.Hourly,     SeedLane.Done,       new(2025,2,3),  new(2025,5,30), null, [3,4],   1),
                new("D-003", "Garðabær — flísalagning baðherb.",       JobCategory.InnerWork,       BillingType.FixedPrice, SeedLane.Done,       new(2025,3,3),  new(2025,5,23), null, [0,5],   2),
                new("D-004", "Keflavíkurflugvöllur — múrviðhald",      JobCategory.Maintenance,     BillingType.Hourly,     SeedLane.Done,       new(2025,4,1),  new(2025,6,27), null, [1,3],   3),
                new("D-005", "Hafnarfjörður — múrbygging búðarhlið",   JobCategory.NewInstallation, BillingType.Hourly,     SeedLane.Done,       new(2025,5,5),  new(2025,7,31), null, [0,2,4], 4),
                // ── InProgress (4) ────────────────────────────────────────────
                new("P-001", "Reykjanesbaer — nýbyggð íbúðahverfi",    JobCategory.NewInstallation, BillingType.Hourly,     SeedLane.InProgress, new(2025,7,1),  new(2025,12,31), null, [0,1,3], 0),
                new("P-002", "Laugarás — stétt og girðingarvinna",     JobCategory.NewInstallation, BillingType.Hourly,     SeedLane.InProgress, new(2025,8,4),  new(2025,12,31), null, [2,4],   1),
                new("P-003", "Dalvík — skólabygging múrvinna",         JobCategory.NewInstallation, BillingType.FixedPrice, SeedLane.InProgress, new(2025,9,1),  new(2025,12,31), null, [0,5],   2),
                new("P-004", "Ísafjörður — húsaviðhald múrverk",       JobCategory.Maintenance,     BillingType.Hourly,     SeedLane.InProgress, new(2025,10,6), new(2025,12,31), null, [6,7],   5),
                // ── Waiting (2) ───────────────────────────────────────────────
                new("W-001", "Suðurnes — hafnarbygging steinvinna",    JobCategory.NewInstallation, BillingType.Hourly,     SeedLane.Waiting,    new(2025,5,5),  new(2025,9,5),  "Beðið eftir hönnunarlýsingu frá arkitekt", [1,2], 3),
                new("W-002", "Borgarnes — kirkjuviðhald",              JobCategory.Repair,          BillingType.FixedPrice, SeedLane.Waiting,    new(2025,6,2),  new(2025,10,3), "Beðið eftir fjármögnun frá safnaðarsjóði",  [0,3], 4),
                // ── Backlog (3) ───────────────────────────────────────────────
                new("B-001", "Egilsstaðir — ný þjónustubygging",       JobCategory.NewInstallation, BillingType.Hourly,     SeedLane.Backlog,    new(2026,1,1),  new(2026,1,1),  null, [], 0),
                new("B-002", "Akureyri — gömul kaupmannahús",          JobCategory.Repair,          BillingType.Hourly,     SeedLane.Backlog,    new(2026,1,1),  new(2026,1,1),  null, [], 1),
                new("B-003", "Selfoss — flísalagning ný skóli",        JobCategory.InnerWork,       BillingType.FixedPrice, SeedLane.Backlog,    new(2026,1,1),  new(2026,1,1),  null, [], 2),
            ],
        };

        return defs.Select((d, i) => new Job
        {
            CompanyId          = companyId,
            CustomerId         = customers[d.CustomerIdx].Id,
            Name               = d.Name,
            Code               = d.Code,
            BillingType        = d.Billing,
            Category           = d.Category,
            JobNumber          = i + 1,
            KanbanStatus       = d.Lane == SeedLane.Waiting ? KanbanStatus.Waiting : KanbanStatus.Active,
            WaitingReason      = d.WaitingReason,
            KanbanInProgressAt = d.Lane is SeedLane.InProgress or SeedLane.Waiting or SeedLane.Done
                                     ? new DateTimeOffset(d.StartDate, TimeOnly.MinValue, TimeSpan.Zero)
                                     : null,
            KanbanWaitingAt    = d.Lane == SeedLane.Waiting
                                     ? new DateTimeOffset(d.EndDate, TimeOnly.MinValue, TimeSpan.Zero)
                                     : null,
            KanbanDoneAt       = d.Lane == SeedLane.Done
                                     ? new DateTimeOffset(d.EndDate.AddDays(3), TimeOnly.MinValue, TimeSpan.Zero)
                                     : null,
        }).ToList();
    }

    // ── Time Entries ──────────────────────────────────────────────────────────

    private static List<TimeEntry> CreateYearEntries(
        Guid companyId, List<Employee> employees, List<Job> jobs, Random rng)
    {
        var entries = new List<TimeEntry>();

        for (var i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];
            var empSlots  = GetEmpSlots(i, jobs.Count);
            var lane      = GetSeedLane(job);
            if (lane == SeedLane.Backlog) continue;

            var start    = job.KanbanInProgressAt!.Value.Date;
            var end      = lane == SeedLane.Done
                               ? job.KanbanDoneAt!.Value.AddDays(-3).Date
                               : lane == SeedLane.Waiting
                                   ? job.KanbanWaitingAt!.Value.Date
                                   : new DateTime(2025, 12, 31);

            var assignedEmps = empSlots
                .Where(s => s < employees.Count)
                .Select(s => employees[s])
                .ToList();

            if (assignedEmps.Count == 0) assignedEmps = [employees[rng.Next(employees.Count)]];

            var invoiced = lane == SeedLane.Done;

            for (var date = DateOnly.FromDateTime(start); date <= DateOnly.FromDateTime(end); date = date.AddDays(1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                if (IsIcelandicHoliday(date)) continue;

                foreach (var emp in assignedEmps)
                {
                    // ~80% attendance per day
                    if (rng.NextDouble() > 0.80) continue;

                    var hours    = rng.Next(6, 9) + (decimal)rng.Next(0, 2) * 0.5m;
                    var overtime = hours > 8 ? hours - 8 : 0;
                    var regular  = hours > 8 ? 8 : hours;
                    var driving  = rng.NextDouble() < 0.25 ? rng.Next(1, 5) : 0;

                    entries.Add(new TimeEntry
                    {
                        CompanyId     = companyId,
                        JobId         = job.Id,
                        EmployeeId    = emp.Id,
                        WorkDate      = date,
                        Hours         = regular,
                        OvertimeHours = overtime,
                        DrivingUnits  = driving,
                        Notes         = "",
                        IsInvoiced    = invoiced,
                        InvoicedAt    = invoiced ? job.KanbanDoneAt : null,
                    });
                }
            }
        }

        return entries;
    }

    private static SeedLane GetSeedLane(Job job)
    {
        if (job.KanbanDoneAt.HasValue)              return SeedLane.Done;
        if (job.KanbanStatus == KanbanStatus.Waiting) return SeedLane.Waiting;
        if (job.KanbanInProgressAt.HasValue)         return SeedLane.InProgress;
        return SeedLane.Backlog;
    }

    // Returns employee index slots for job i out of total jobs
    private static int[] GetEmpSlots(int jobIndex, int totalJobs)
    {
        // Spread employees across jobs in a round-robin fashion
        // Each job gets 2-3 employees from the 9-person pool
        return jobIndex % 3 == 0
            ? [jobIndex % 9, (jobIndex + 1) % 9, (jobIndex + 2) % 9]
            : [jobIndex % 9, (jobIndex + 1) % 9];
    }

    // ── Absence Requests ──────────────────────────────────────────────────────

    private static List<AbsenceRequest> CreateAbsences(
        Guid companyId, List<Employee> employees, Guid reviewerId, Random rng)
    {
        var absences = new List<AbsenceRequest>();
        var now      = DateTime.UtcNow;

        foreach (var (emp, idx) in employees.Select((e, i) => (e, i)))
        {
            // Summer vacation (~2 weeks in July, staggered by employee)
            var vacStart = new DateOnly(2025, 7, 1).AddDays(idx * 3 % 14);
            absences.Add(new AbsenceRequest
            {
                CompanyId    = companyId,
                EmployeeId   = emp.Id,
                Type         = AbsenceType.Vacation,
                Status       = AbsenceStatus.Approved,
                StartDate    = vacStart,
                EndDate      = vacStart.AddDays(13),
                Notes        = "Sumarfrí",
                ReviewedBy   = reviewerId,
                ReviewedAt   = now.AddMonths(-3),
                CreatedAt    = now.AddMonths(-4),
            });

            // Sick leave (1–3 days, scattered through the year)
            var sickMonth = (idx % 10) + 1;
            if (sickMonth == 7) sickMonth = 8; // avoid colliding with vacation month
            var sickStart = new DateOnly(2025, sickMonth, Math.Min(idx * 4 % 20 + 2, 25));
            var sickDays  = rng.Next(1, 4);
            absences.Add(new AbsenceRequest
            {
                CompanyId    = companyId,
                EmployeeId   = emp.Id,
                Type         = AbsenceType.SickLeave,
                Status       = AbsenceStatus.Approved,
                StartDate    = sickStart,
                EndDate      = sickStart.AddDays(sickDays - 1),
                Notes        = "Veikindi",
                ReviewedBy   = reviewerId,
                ReviewedAt   = now.AddDays(-(365 - sickMonth * 30)),
                CreatedAt    = now.AddDays(-(365 - sickMonth * 30 + 1)),
            });

            // Sick child leave for employees 0, 3, 6
            if (idx % 3 == 0)
            {
                var childMonth = idx % 2 == 0 ? 3 : 10;
                var childStart = new DateOnly(2025, childMonth, 10 + idx % 10);
                absences.Add(new AbsenceRequest
                {
                    CompanyId    = companyId,
                    EmployeeId   = emp.Id,
                    Type         = AbsenceType.SickChildLeave,
                    Status       = AbsenceStatus.Approved,
                    StartDate    = childStart,
                    EndDate      = childStart.AddDays(1),
                    Notes        = "Barn veikt",
                    ReviewedBy   = reviewerId,
                    ReviewedAt   = now.AddDays(-(365 - childMonth * 30)),
                    CreatedAt    = now.AddDays(-(365 - childMonth * 30 + 1)),
                });
            }

            // Parental leave for employees 2 and 7
            if (idx == 2)
            {
                absences.Add(new AbsenceRequest
                {
                    CompanyId    = companyId,
                    EmployeeId   = emp.Id,
                    Type         = AbsenceType.ParentalLeave,
                    Status       = AbsenceStatus.Approved,
                    StartDate    = new DateOnly(2025, 2, 1),
                    EndDate      = new DateOnly(2025, 4, 30),
                    Notes        = "Fæðingarorlof",
                    ReviewedBy   = reviewerId,
                    ReviewedAt   = now.AddMonths(-6),
                    CreatedAt    = now.AddMonths(-7),
                });
            }

            // Christmas leave (pending or approved depending on employee)
            if (idx < 5)
            {
                absences.Add(new AbsenceRequest
                {
                    CompanyId    = companyId,
                    EmployeeId   = emp.Id,
                    Type         = AbsenceType.Vacation,
                    Status       = idx < 3 ? AbsenceStatus.Approved : AbsenceStatus.Pending,
                    StartDate    = new DateOnly(2025, 12, 22),
                    EndDate      = new DateOnly(2025, 12, 31),
                    Notes        = "Jólafrí",
                    ReviewedBy   = idx < 3 ? reviewerId : null,
                    ReviewedAt   = idx < 3 ? now.AddDays(-30) : null,
                    CreatedAt    = now.AddDays(-45),
                });
            }

            // One denied request per company (employee 4)
            if (idx == 4)
            {
                absences.Add(new AbsenceRequest
                {
                    CompanyId    = companyId,
                    EmployeeId   = emp.Id,
                    Type         = AbsenceType.PersonalLeave,
                    Status       = AbsenceStatus.Denied,
                    StartDate    = new DateOnly(2025, 9, 15),
                    EndDate      = new DateOnly(2025, 9, 19),
                    Notes        = "Persónulegar ástæður",
                    ReviewNotes  = "Ekki hægt að gefa lausn á þessum tíma — mikið verkefni í gangi",
                    ReviewedBy   = reviewerId,
                    ReviewedAt   = now.AddMonths(-4),
                    CreatedAt    = now.AddMonths(-5),
                });
            }
        }

        return absences;
    }

    // ── Material Usages ───────────────────────────────────────────────────────

    private static List<MaterialUsage> CreateMaterialUsages(
        Guid companyId, List<Employee> employees, List<Job> jobs, List<Material> materials, Random rng)
    {
        var usages = new List<MaterialUsage>();

        // Only attach usages to InProgress, Waiting, and Done jobs
        var eligibleJobs = jobs.Where(j => j.KanbanInProgressAt.HasValue).ToList();

        foreach (var job in eligibleJobs)
        {
            var lane     = GetSeedLane(job);
            var jobStart = job.KanbanInProgressAt!.Value;
            var jobEnd   = lane == SeedLane.Done
                               ? job.KanbanDoneAt!.Value.AddDays(-1)
                               : lane == SeedLane.Waiting
                                   ? job.KanbanWaitingAt!.Value.AddDays(-1)
                                   : new DateTimeOffset(2025, 12, 15, 0, 0, 0, TimeSpan.Zero);

            // 2-4 material usages per job
            var count = rng.Next(2, 5);
            for (var i = 0; i < count; i++)
            {
                var mat = materials[rng.Next(materials.Count)];
                var emp = employees[rng.Next(employees.Count)];

                var spanDays = (int)(jobEnd - jobStart).TotalDays;
                if (spanDays < 1) spanDays = 1;
                var usedAt = jobStart.AddDays(rng.Next(0, spanDays));

                usages.Add(new MaterialUsage
                {
                    CompanyId  = companyId,
                    MaterialId = mat.Id,
                    EmployeeId = emp.Id,
                    JobId      = job.Id,
                    Quantity   = rng.Next(1, 20),
                    UsedAt     = usedAt,
                    Notes      = "",
                    IsInvoiced = lane == SeedLane.Done,
                    InvoicedAt = lane == SeedLane.Done ? job.KanbanDoneAt : null,
                });
            }
        }

        return usages;
    }

    // ── Icelandic public holidays ─────────────────────────────────────────────

    private static bool IsIcelandicHoliday(DateOnly d) => (d.Month, d.Day) switch
    {
        (1, 1)  => true, // Nýársdagur
        (1, 6)  => true, // Þrettándinn
        (3, 20) => true, // Skírdagur (approx)
        (3, 21) => true, // Föstudagurinn langi (approx)
        (3, 23) => true, // Páskadagur (approx)
        (3, 24) => true, // Annar í páskum (approx)
        (4, 24) => true, // Sumardagurinn fyrsti (approx)
        (5, 1)  => true, // Verkalýðsdagurinn
        (5, 29) => true, // Uppstigningardagur (approx)
        (5, 11) => true, // Hvítasunna (approx)
        (5, 12) => true, // Annar í hvítasunnu (approx)
        (6, 17) => true, // Þjóðhátíðardagurinn
        (8, 4)  => true, // Frídagur verslunarmanna (first Mon Aug, approx)
        (12, 24) => true,
        (12, 25) => true,
        (12, 26) => true,
        (12, 31) => true,
        _ => false
    };
}
