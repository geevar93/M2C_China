using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SourcingOps.Infrastructure.Persistence;

/// <summary>
/// Used exclusively by `dotnet ef migrations add` / `dotnet ef database update` at
/// design time. Its presence means EF tooling never has to execute the real
/// `Program.cs` composition root (JWT signing-key fail-fast check, DB seeding, etc.)
/// just to generate or apply a migration — it builds a minimal, standalone
/// `AppDbContext` instead. The naming convention MUST match `DependencyInjection.AddInfrastructure`
/// exactly, or generated migrations will disagree with the runtime model.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=sourcingops;Username=app;Password=changeme";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();

        return new AppDbContext(optionsBuilder.Options);
    }
}
