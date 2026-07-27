using Microsoft.EntityFrameworkCore;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Application.Tests.TestSupport;

/// <summary>Builds a fresh EF Core InMemory-backed AppDbContext per test — fast, isolated, no Docker required.</summary>
public static class TestDbContextFactory
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
