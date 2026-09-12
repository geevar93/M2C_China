using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SourcingOps.Infrastructure;
using SourcingOps.Infrastructure.Persistence;

// Standalone migrator (mirrors KlaraHome's dedicated migrator project/image): the
// runtime `api` image deliberately has no SDK layer, so `dotnet ef database update`
// can't run inside it (TECH_SPEC §6/§7). This console app carries just enough of
// Infrastructure's DI (AppDbContext + its Npgsql/caching/storage option bindings, all
// no-ops here) to call `Database.MigrateAsync()` directly, ships as its own small
// runtime image (see Dockerfile), and is invoked explicitly via the `migrate` compose
// profile — never automatically, and never by rebuilding from source on the VPS.

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddInfrastructure(configuration);
await using var provider = services.BuildServiceProvider();

await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

try
{
    var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
    if (pending.Count == 0)
    {
        Console.WriteLine("No pending migrations — database is up to date.");
        return 0;
    }

    Console.WriteLine($"Applying {pending.Count} pending migration(s): {string.Join(", ", pending)}");
    await db.Database.MigrateAsync();
    Console.WriteLine("Migrations applied successfully.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Migration failed: {ex}");
    return 1;
}
