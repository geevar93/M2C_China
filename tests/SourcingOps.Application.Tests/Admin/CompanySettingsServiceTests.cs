using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SourcingOps.Application.Admin;
using SourcingOps.Application.Interfaces;
using SourcingOps.Application.Tests.TestSupport;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Application.Tests.Admin;

/// <summary>
/// Covers M6 contract §0's mechanism: the singleton is genuinely absent until the first PUT,
/// GetAsync synthesizes an all-null shape rather than 404ing, and every field round-trips
/// through an upsert including the second call (update, not a second insert).
/// </summary>
public class CompanySettingsServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private static CompanySettingsService CreateSut(AppDbContext db, out Mock<IAuditLogger> auditMock)
    {
        auditMock = new Mock<IAuditLogger>();
        return new CompanySettingsService(db, auditMock.Object);
    }

    private static UpsertCompanySettingsRequest ValidRequest() => new(
        "Meridian Sourcing Pvt Ltd", "24AAAAA0000A1Z5", "123 Industrial Estate, Surat, Gujarat",
        "Meridian Sourcing", "000123456789", "HDFC0000123", "Surat Main", "INV", "Goods once sold will not be taken back.");

    [Fact]
    public async Task GetAsync_NoRowYet_ReturnsAllNullShape()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var result = await sut.GetAsync();

        result.LegalEntityName.Should().BeNull();
        result.RegisteredAddress.Should().BeNull();
        result.UpdatedAt.Should().BeNull();
        result.UpdatedByName.Should().BeNull();
    }

    [Fact]
    public async Task UpsertAsync_FirstCall_CreatesTheSingletonRow()
    {
        using var db = TestDbContextFactory.Create();
        db.Users.Add(new User { Id = Actor, Name = "Priya Sharma", Email = $"admin-{Guid.NewGuid():N}@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out var auditMock);

        var result = await sut.UpsertAsync(ValidRequest(), Actor);

        result.LegalEntityName.Should().Be("Meridian Sourcing Pvt Ltd");
        result.RegisteredAddress.Should().Be("123 Industrial Estate, Surat, Gujarat");
        result.UpdatedAt.Should().NotBeNull();
        result.UpdatedByName.Should().Be("Priya Sharma");
        (await db.CompanySettings.CountAsync()).Should().Be(1);
        auditMock.Verify(a => a.LogAsync(Actor, "CompanySettingsCreated", "CompanySettings",
            CompanySettings.SingletonId.ToString(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertAsync_SecondCall_UpdatesTheSameRow_NeverInsertsASecondOne()
    {
        using var db = TestDbContextFactory.Create();
        db.Users.Add(new User { Id = Actor, Name = "Priya Sharma", Email = $"admin-{Guid.NewGuid():N}@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out var auditMock);
        await sut.UpsertAsync(ValidRequest(), Actor);

        var result = await sut.UpsertAsync(ValidRequest() with { Gstin = "29BBBBB1111B2Z6" }, Actor);

        result.Gstin.Should().Be("29BBBBB1111B2Z6");
        (await db.CompanySettings.CountAsync()).Should().Be(1, "the CHECK constraint on the real DB backs this too, but the service must never even attempt a second insert");
        auditMock.Verify(a => a.LogAsync(Actor, "CompanySettingsUpdated", "CompanySettings",
            CompanySettings.SingletonId.ToString(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertAsync_BlankStrings_AreStoredAsNull()
    {
        using var db = TestDbContextFactory.Create();
        db.Users.Add(new User { Id = Actor, Name = "Priya Sharma", Email = $"admin-{Guid.NewGuid():N}@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _);

        var result = await sut.UpsertAsync(ValidRequest() with { Gstin = "   " }, Actor);

        result.Gstin.Should().BeNull();
    }
}
