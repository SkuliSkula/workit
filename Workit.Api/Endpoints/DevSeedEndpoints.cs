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
            // Safety: only run if no owner users exist yet
            if (await db.AppUsers.AnyAsync(u => u.Role == WorkitRoles.Owner, ct))
                return Results.BadRequest("Seed data already exists. Clear the database first.");

            const string password = "Test1234!";
            var rng = new Random(42);

            // ── Companies ─────────────────────────────────────────────────────
            var compA = new Company { Id = Guid.NewGuid(), Name = "Rafvirki ehf.",   Ssn = "5501012340", Email = "rafvirki@test.is",   Phone = "5551000", Address = "Ármúli 1, 108 Reykjavík",  Owner = "Jón Sigurðsson",  DrivingUnitPrice = 120, StandardHoursPerDay = 8 };
            var compB = new Company { Id = Guid.NewGuid(), Name = "Pípulagning hf.", Ssn = "6601023450", Email = "pipu@test.is",        Phone = "5552000", Address = "Borgartún 5, 105 Reykjavík", Owner = "Jón Sigurðsson",  DrivingUnitPrice = 120, StandardHoursPerDay = 8 };
            var compC = new Company { Id = Guid.NewGuid(), Name = "Múrverk sf.",     Ssn = "7701034560", Email = "murverk@test.is",     Phone = "5553000", Address = "Suðurlandsbraut 12, 108 Reykjavík", Owner = "María Ólafsdóttir", DrivingUnitPrice = 100, StandardHoursPerDay = 8 };
            db.Companies.AddRange(compA, compB, compC);

            // ── Owners ────────────────────────────────────────────────────────
            var owner1 = new AppUser { Id = Guid.NewGuid(), Name = "Jón Sigurðsson",    Email = "jon@test.is",   PasswordHash = PasswordHasher.HashPassword(password), Role = WorkitRoles.Owner, CompanyId = compA.Id };
            var owner2 = new AppUser { Id = Guid.NewGuid(), Name = "María Ólafsdóttir", Email = "maria@test.is", PasswordHash = PasswordHasher.HashPassword(password), Role = WorkitRoles.Owner, CompanyId = compC.Id };
            db.AppUsers.AddRange(owner1, owner2);

            // ── UserCompanies (owner1 → both, owner2 → compC) ─────────────────
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

            // ── Jobs ──────────────────────────────────────────────────────────
            var jobsA = CreateJobs(compA.Id, custA);
            var jobsB = CreateJobs(compB.Id, custB);
            var jobsC = CreateJobs(compC.Id, custC);
            db.Jobs.AddRange([.. jobsA, .. jobsB, .. jobsC]);

            // ── Employees + AppUsers ───────────────────────────────────────────
            var (empsA, usersA) = CreateEmployees(compA.Id, password, "Rafvirki ehf.",   rng);
            var (empsB, usersB) = CreateEmployees(compB.Id, password, "Pípulagning hf.", rng);
            var (empsC, usersC) = CreateEmployees(compC.Id, password, "Múrverk sf.",     rng);
            db.Employees.AddRange([.. empsA, .. empsB, .. empsC]);
            db.AppUsers.AddRange([.. usersA, .. usersB, .. usersC]);

            // ── Tools ─────────────────────────────────────────────────────────
            var toolsA = CreateTools(compA.Id, "Rafvirki ehf.");
            var toolsB = CreateTools(compB.Id, "Pípulagning hf.");
            var toolsC = CreateTools(compC.Id, "Múrverk sf.");
            db.Tools.AddRange([.. toolsA, .. toolsB, .. toolsC]);

            // ── Materials ─────────────────────────────────────────────────────
            var matsA = CreateMaterials(compA.Id, "electrical");
            var matsB = CreateMaterials(compB.Id, "plumbing");
            var matsC = CreateMaterials(compC.Id, "masonry");
            db.Materials.AddRange([.. matsA, .. matsB, .. matsC]);

            // ── Time Entries (Oct 2025 – Mar 2026) ────────────────────────────
            var entries = new List<TimeEntry>();
            entries.AddRange(CreateEntries(compA.Id, empsA, jobsA, rng));
            entries.AddRange(CreateEntries(compB.Id, empsB, jobsB, rng));
            entries.AddRange(CreateEntries(compC.Id, empsC, jobsC, rng));
            db.TimeEntries.AddRange(entries);

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                companies    = 3,
                owners       = 2,
                customers    = custA.Count + custB.Count + custC.Count,
                jobs         = jobsA.Count + jobsB.Count + jobsC.Count,
                employees    = empsA.Count + empsB.Count + empsC.Count,
                tools        = toolsA.Count + toolsB.Count + toolsC.Count,
                materials    = matsA.Count + matsB.Count + matsC.Count,
                timeEntries  = entries.Count,
                credentials  = new { email1 = "jon@test.is", email2 = "maria@test.is", password }
            });
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<Customer> CreateCustomers(Guid companyId) =>
    [
        new() { CompanyId = companyId, Name = "Bygg ehf.",         Ssn = "4401012340", Email = "bygg@test.is",         Phone = "4441000", ContactPerson = "Anna Björnsdóttir" },
        new() { CompanyId = companyId, Name = "Íbúðafélagið hf.",  Ssn = "5502024560", Email = "ibuda@test.is",        Phone = "4442000", ContactPerson = "Gunnar Pétursson"  },
        new() { CompanyId = companyId, Name = "Storkur sf.",        Ssn = "6603036780", Email = "storkur@test.is",      Phone = "4443000", ContactPerson = "Sigríður Magnúsdóttir" },
        new() { CompanyId = companyId, Name = "Norðurljós ehf.",    Ssn = "7704048900", Email = "nordurljós@test.is",   Phone = "4444000", ContactPerson = "Einar Stefánsson"  },
    ];

    private static List<Job> CreateJobs(Guid companyId, List<Customer> customers) =>
    [
        new() { CompanyId = companyId, CustomerId = customers[0].Id, Name = "Verkefni A",    Code = "V-001" },
        new() { CompanyId = companyId, CustomerId = customers[0].Id, Name = "Verkefni B",    Code = "V-002" },
        new() { CompanyId = companyId, CustomerId = customers[1].Id, Name = "Húsbyggingar",  Code = "H-001" },
        new() { CompanyId = companyId, CustomerId = customers[2].Id, Name = "Viðhald 2025",  Code = "VH-01" },
        new() { CompanyId = companyId, CustomerId = customers[3].Id, Name = "Nýbygging",     Code = "NB-01" },
    ];

    private static (List<Employee> employees, List<AppUser> users) CreateEmployees(
        Guid companyId, string password, string companyName, Random rng)
    {
        string[] trades = ["Rafvirki", "Pípulagningamaður", "Múrari", "Trésmíðamaður", "Málari"];
        string[] firstNames = ["Ólafur", "Guðmundur", "Sigurður", "Jón", "Gunnar", "Kristján", "Bjarni", "Helgi", "Magnús", "Eiríkur"];
        string[] lastNames  = ["Jónsson", "Sigurðsson", "Gunnarsson", "Björnsson", "Pétursson", "Ólafsson", "Einarsson", "Stefánsson", "Kristjánsson", "Magnússon"];

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
                Id                = Guid.NewGuid(),
                CompanyId         = companyId,
                DisplayName       = $"{fn} {ln}",
                Trade             = trade,
                Ssn               = $"{2001 + i:D4}01{i + 1:D2}490",
                Email             = $"{fn.ToLowerInvariant()}.{ln.ToLowerInvariant()}@{slug}.is",
                Phone             = $"77{i:D2}000",
                HourlySalary      = 2800 + rng.Next(0, 800),
                HourlyBillableRate = 5000 + rng.Next(0, 2000),
                EmploymentType    = i < 7 ? EmploymentType.Employed : EmploymentType.Contractor,
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

    private static List<Tool> CreateTools(Guid companyId, string companyName)
    {
        string[] electricalTools = ["Dremel 4300", "Makita DHP484 borvél", "Fluke 117 rafmagnsmælir", "Klein Tools snyrting", "Milwaukee M12 ljós", "Bosch GSB 18V-55 borvél", "Hilti TE 2-A22 hamar", "Fluke 376 straumtang", "Klauke ES 32 þjöppunartól", "Leitarljós Peli 9430", "Dewalt DCS361 sirkelsög", "Ridgid RT-12 þráðfléttur", "Bosch GTC 400 C hitamyndavél", "Leica D2 laserregel", "Makita DGA504 slípivél"];
        string[] plumbingTools  = ["Rothenberger ROMAX borvél", "Ridgid 258 þráðfléttur", "Virax 232610 þrýstipróf", "Rothenberger Rotest GE prufa", "Hilti DX 460 þrýstibyssu", "Reed VPCL frostlykill", "Rems Eco-Press þjöppunartól", "Milwaukee M18 þrykkjupumpa", "Bosch GAS 25 L SFC ryksuga", "Fluke 922 loftþrýstimælir", "Makita HR2470 hamar", "Ridgid WS-1230 söggur", "Leica X3 laserregel", "Dewalt DCD791 snúningsborvél", "Metabo HPT C10FCG sög"];
        string[] masonryTools   = ["Hilti TE 70-ATC/AVR hamar", "Makita HM1812X3 lofthamar", "Bosch GBH 5-40 DE hamar", "Dewalt D25333K hamar", "Husqvarna DM 220 kjarnaborar", "Stihl TS 410 skurðartól", "Rubi DC-250 1200 flísasög", "Bosch GKS 85 G sirkelsög", "Festool TS 75 EBQ sög", "Leica Rugby 640 laserregel", "Milwaukee M18 GG smurningarbyssu", "Dewalt DCS391 hliðarsög", "Metabo WB 18 LTX BL 125 slípivél", "Bosch GWS 22-230 LVI slípivél", "Makita DGA900 slípivél"];

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

    private static List<Material> CreateMaterials(Guid companyId, string trade)
    {
        var items = trade switch
        {
            "electrical" => new[]
            {
                ("N1XE-U 5G 1.5 Cu 1kV leiðari",      "EL-001", "Leiðarar",    "m.",   500m,  420m,  630m),
                ("N1XE-U 5G 2.5 Cu 1kV leiðari",      "EL-002", "Leiðarar",    "m.",   800m,  580m,  870m),
                ("N1XE-U 5G 6 Cu 1kV leiðari",        "EL-003", "Leiðarar",    "m.",   300m,  920m, 1380m),
                ("NYM-J 3x1.5 leiðari",                "EL-004", "Leiðarar",    "m.",   600m,  290m,  435m),
                ("NYM-J 3x2.5 leiðari",                "EL-005", "Leiðarar",    "m.",   400m,  390m,  585m),
                ("Schneider iC60 16A B-rof",           "EL-010", "Verndarar",   "stk.", 200m,  650m,  975m),
                ("Schneider iC60 25A C-rof",           "EL-011", "Verndarar",   "stk.", 150m,  780m, 1170m),
                ("Schneider iC60 32A C-rof",           "EL-012", "Verndarar",   "stk.", 100m,  890m, 1335m),
                ("ABB F204 30mA jarðtenglavörn",       "EL-013", "Verndarar",   "stk.",  80m, 3200m, 4800m),
                ("Hager MGB116A mælaborð 16-eininga",  "EL-020", "Mælaborð",    "stk.",  10m, 28000m,42000m),
                ("Legrand 774160 tjakkdós",            "EL-030", "Dósir",       "stk.", 500m,  180m,  270m),
                ("Legrand 774162 útfallsdós",          "EL-031", "Dósir",       "stk.", 400m,  220m,  330m),
                ("Schneider Odace ljósrofi",           "EL-040", "Rofahlutir",  "stk.", 300m,  520m,  780m),
                ("Schneider Odace 16A innstunga",      "EL-041", "Rofahlutir",  "stk.", 350m,  680m, 1020m),
                ("Philips CorePro LED 9W E27",         "EL-050", "Lýsing",      "stk.", 200m,  890m, 1335m),
                ("Philips CorePro LED 15W E27",        "EL-051", "Lýsing",      "stk.", 100m, 1150m, 1725m),
                ("Osram Ledvance panel 60x60 36W",     "EL-052", "Lýsing",      "stk.",  50m, 7800m,11700m),
                ("DIN listi 35mm 1m",                  "EL-060", "Fylgihlutir", "stk.", 100m,  480m,  720m),
                ("Kabelskór 2.5mm² blár 100 stk",      "EL-061", "Fylgihlutir", "pk.",  200m,  690m, 1035m),
                ("Varaplástur PVC 20mm grár 50m",      "EL-062", "Fylgihlutir", "rúll.", 80m, 1200m, 1800m),
            },
            "plumbing" => new[]
            {
                ("Uponor 16mm PE-Xa lagnir 100m",      "PL-001", "Lagnir",      "rúll.", 20m, 18500m,27750m),
                ("Uponor 20mm PE-Xa lagnir 50m",       "PL-002", "Lagnir",      "rúll.", 15m, 14200m,21300m),
                ("Uponor 25mm PE-Xa lagnir 25m",       "PL-003", "Lagnir",      "rúll.", 10m, 12800m,19200m),
                ("Copper 15mm þykkt 3m",               "PL-004", "Lagnir",      "stk.", 200m,  2200m, 3300m),
                ("Copper 22mm þykkt 3m",               "PL-005", "Lagnir",      "stk.", 100m,  3800m, 5700m),
                ("Uponor Q&E 16mm bogasamskeyti",      "PL-010", "Samskeyti",   "stk.", 500m,   620m,  930m),
                ("Uponor Q&E 20mm bogasamskeyti",      "PL-011", "Samskeyti",   "stk.", 300m,   820m, 1230m),
                ("Uponor Q&E 16x1/2 veggsamskeyti",   "PL-012", "Samskeyti",   "stk.", 200m,   980m, 1470m),
                ("Geberit Duofix WC rammi",            "PL-020", "Salernistæki","stk.",  20m, 42000m,63000m),
                ("Geberit Sigma20 þvottaklafi",        "PL-021", "Salernistæki","stk.",  25m, 18500m,27750m),
                ("Grohe Eurosmart blöndunarr 35mm",    "PL-030", "Blöndunartæki","stk.", 30m, 28000m,42000m),
                ("Grohe Eurosmart sturtuhaus",         "PL-031", "Blöndunartæki","stk.", 20m, 32000m,48000m),
                ("Ballofix 15mm lokavatn",             "PL-040", "Lokar",       "stk.", 100m,  2800m, 4200m),
                ("Ballofix 22mm lokavatn",             "PL-041", "Lokar",       "stk.",  80m,  4200m, 6300m),
                ("Fernox F1 kvarðaefni 500ml",         "PL-050", "Kvarðaefni",  "stk.",  50m,  3200m, 4800m),
                ("Sievert blásari kit 3485",           "PL-051", "Kvarðaefni",  "stk.",  10m, 18000m,27000m),
                ("Rothenberger þræðaolía 1L",          "PL-052", "Kvarðaefni",  "stk.",  30m,  1800m, 2700m),
                ("Mepla 16mm PE-Xc 5m",                "PL-060", "Lagnir",      "stk.", 100m,  4200m, 6300m),
                ("Isover Rörskål 22/30mm 1m",          "PL-070", "Einangrun",   "stk.", 200m,   980m, 1470m),
                ("Isover Rörskål 28/30mm 1m",          "PL-071", "Einangrun",   "stk.", 150m,  1250m, 1875m),
            },
            _ => new[] // masonry
            {
                ("Portland sement CEM I 42.5 25kg",    "MU-001", "Sement",      "stk.", 400m,  1800m, 2700m),
                ("Portland sement CEM II 32.5 25kg",   "MU-002", "Sement",      "stk.", 200m,  1600m, 2400m),
                ("Múrblanda M5 25kg",                   "MU-003", "Múrblanda",   "stk.", 500m,  1200m, 1800m),
                ("Múrblanda M10 25kg",                  "MU-004", "Múrblanda",   "stk.", 300m,  1400m, 2100m),
                ("Porobeton B6 600x300x200",            "MU-010", "Steinn",      "stk.", 500m,   980m, 1470m),
                ("Porobeton B4 600x300x150",            "MU-011", "Steinn",      "stk.", 400m,   780m, 1170m),
                ("Leca blokk 40x20x20 standard",       "MU-012", "Steinn",      "stk.", 800m,   620m,  930m),
                ("Leca blokk 40x20x15 þunnt",          "MU-013", "Steinn",      "stk.", 600m,   520m,  780m),
                ("Múrkamb gr. 50 3m",                  "MU-020", "Búnaður",     "stk.", 100m,  2800m, 4200m),
                ("Hilti HIT-RE 500 v3 lím 330ml",      "MU-021", "Festar",      "stk.",  80m,  6800m,10200m),
                ("Hilti HST3-R M10x90 festibolti",     "MU-022", "Festar",      "pk.",  200m,  4200m, 6300m),
                ("Fischer FIS V 360 S festar",         "MU-023", "Festar",      "stk.",  60m,  7200m,10800m),
                ("Mapei Keraflex maxi S1 25kg",        "MU-030", "Flísalím",    "stk.",  80m,  4800m, 7200m),
                ("Mapei Ultracolor plus FA 5kg",       "MU-031", "Flísalím",    "stk.", 100m,  3200m, 4800m),
                ("Rockwool Flexi einangrunarplata",     "MU-040", "Einangrun",   "stk.", 200m,  3800m, 5700m),
                ("Isover Glasswool 100mm 6m²",         "MU-041", "Einangrun",   "stk.", 150m,  4200m, 6300m),
                ("Bramac Express taksteinn",            "MU-050", "Þak",         "stk.", 300m,  1200m, 1800m),
                ("Icopal bitumenvara 10m²",            "MU-051", "Þak",         "rúll.", 40m, 12000m,18000m),
                ("Gyproc GN13 gipsplata 2.6m",        "MU-060", "Gips",        "stk.", 250m,  2800m, 4200m),
                ("Gyproc GEK13 eldfastplata 2.6m",    "MU-061", "Gips",        "stk.", 100m,  3400m, 5100m),
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

    private static List<TimeEntry> CreateEntries(
        Guid companyId, List<Employee> employees, List<Job> jobs, Random rng)
    {
        var entries   = new List<TimeEntry>();
        var startDate = new DateOnly(2025, 10, 1);
        var endDate   = new DateOnly(2026,  3, 31);

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            // Skip weekends
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;

            foreach (var emp in employees)
            {
                // ~85% chance employee works on a given day
                if (rng.NextDouble() > 0.85) continue;

                var job     = jobs[rng.Next(jobs.Count)];
                var hours   = rng.Next(6, 10) + (decimal)rng.Next(0, 2) * 0.5m;
                var overtime = hours > 8 ? hours - 8 : 0;
                var regularHours = hours > 8 ? 8 : hours;
                var driving = rng.NextDouble() < 0.3 ? rng.Next(1, 6) : 0;

                entries.Add(new TimeEntry
                {
                    CompanyId     = companyId,
                    JobId         = job.Id,
                    EmployeeId    = emp.Id,
                    WorkDate      = date,
                    Hours         = regularHours,
                    OvertimeHours = overtime,
                    DrivingUnits  = driving,
                    Notes         = "",
                    IsInvoiced    = date < new DateOnly(2026, 2, 1) && rng.NextDouble() < 0.7,
                });
            }
        }

        return entries;
    }
}
