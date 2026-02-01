using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Workit.Api.Data;

public sealed class WorkitDbContextFactory : IDesignTimeDbContextFactory<WorkitDbContext>
{
    public WorkitDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("WorkitDb")
            ?? "Server=localhost,1433;Database=WorkitDb;User Id=sa;Password=Your_password123;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<WorkitDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new WorkitDbContext(optionsBuilder.Options);
    }
}
