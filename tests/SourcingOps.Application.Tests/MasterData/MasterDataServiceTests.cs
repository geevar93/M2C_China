using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Application.MasterData;
using SourcingOps.Application.Tests.TestSupport;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Caching;

namespace SourcingOps.Application.Tests.MasterData;

/// <summary>
/// Covers ACTION_PLAN E3-03…E3-09. Uses the REAL <see cref="MemoryCacheService"/> (not a
/// fake) so the E3-09 cache-then-invalidate-then-fresh-read assertions exercise the actual
/// production cache path, not a test double that could silently diverge from it.
/// </summary>
public class MasterDataServiceTests
{
    private static MasterDataService CreateSut(SourcingOps.Infrastructure.Persistence.AppDbContext db, out Mock<IAuditLogger> auditMock, ICacheService? cache = null)
    {
        auditMock = new Mock<IAuditLogger>();
        cache ??= new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));
        return new MasterDataService(db, cache, auditMock.Object);
    }

    private static readonly Guid Actor = Guid.NewGuid();

    // ---- Aggregate read / retired filtering --------------------------------

    [Fact]
    public async Task GetAggregateAsync_DefaultExcludesRetiredRows()
    {
        using var db = TestDbContextFactory.Create();
        db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Active Cat", IsActive = true, SortOrder = 1 });
        db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Retired Cat", IsActive = false, SortOrder = 2 });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _);

        var result = await sut.GetAggregateAsync(includeRetired: false);

        result.Categories.Should().ContainSingle(c => c.Name == "Active Cat");
        result.Categories.Should().NotContain(c => c.Name == "Retired Cat");
    }

    [Fact]
    public async Task GetAggregateAsync_IncludeRetiredTrue_ReturnsEverything()
    {
        using var db = TestDbContextFactory.Create();
        db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Active Cat", IsActive = true, SortOrder = 1 });
        db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Retired Cat", IsActive = false, SortOrder = 2 });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _);

        var result = await sut.GetAggregateAsync(includeRetired: true);

        result.Categories.Should().HaveCount(2);
        result.Categories.Should().Contain(c => c.Name == "Retired Cat" && !c.IsActive);
    }

    [Fact]
    public async Task GetAggregateAsync_CategoriesUseNameShape_OthersUseCodeLabelShape()
    {
        using var db = TestDbContextFactory.Create();
        db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Jewellery", IsActive = true, SortOrder = 1 });
        db.ServiceTypes.Add(new ServiceType { Id = Guid.NewGuid(), Code = "CIF", Label = "CIF", IsActive = true, SortOrder = 1 });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _);

        var result = await sut.GetAggregateAsync(includeRetired: false);

        result.Categories.Single().Name.Should().Be("Jewellery");
        result.ServiceTypes.Single().Code.Should().Be("CIF");
        result.ServiceTypes.Single().Label.Should().Be("CIF");
    }

    // ---- E3-09: cache populated on read, invalidated on write --------------

    [Fact]
    public async Task GetAggregateAsync_SecondReadIsServedFromCache_UntilAWriteInvalidatesIt()
    {
        using var db = TestDbContextFactory.Create();
        var cache = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));
        var sut = CreateSut(db, out _, cache);

        var firstRead = await sut.GetAggregateAsync(includeRetired: false);
        firstRead.Categories.Should().BeEmpty();

        // Insert directly, bypassing the service — proves the SECOND read is cache-served
        // (stale) rather than hitting the DB again, until a service-level write invalidates it.
        db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Bypassed Insert", IsActive = true, SortOrder = 1 });
        await db.SaveChangesAsync();

        var stillCached = await sut.GetAggregateAsync(includeRetired: false);
        stillCached.Categories.Should().BeEmpty("the aggregate is cached and nothing has invalidated it yet");

        // Now perform a real write through the service — this must invalidate the cache.
        await sut.CreateAsync(MasterDataCollectionKey.Categories, new UpsertMasterDataRequest("New Cat", null, null), Actor);

        var freshRead = await sut.GetAggregateAsync(includeRetired: false);
        freshRead.Categories.Select(c => c.Name).Should().BeEquivalentTo(["Bypassed Insert", "New Cat"]);
    }

    // ---- Create -------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_Category_PersistsAndAudits()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out var audit);

        var result = await sut.CreateAsync(MasterDataCollectionKey.Categories, new UpsertMasterDataRequest("Toys", null, null), Actor);

        var dto = result.Should().BeOfType<CategoryDto>().Subject;
        dto.Name.Should().Be("Toys");
        dto.IsActive.Should().BeTrue();
        db.Categories.Should().ContainSingle(c => c.Name == "Toys");
        audit.Verify(a => a.LogAsync(Actor, "MasterDataCreated", "Category", dto.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_LookupCollection_PersistsCodeAndLabel()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out var audit);

        var result = await sut.CreateAsync(MasterDataCollectionKey.ShipmentStatuses, new UpsertMasterDataRequest(null, "READY", "Ready"), Actor);

        var dto = result.Should().BeOfType<LookupItemDto>().Subject;
        dto.Code.Should().Be("READY");
        dto.Label.Should().Be("Ready");
        db.ShipmentStatuses.Should().ContainSingle(s => s.Code == "READY");
        audit.Verify(a => a.LogAsync(Actor, "MasterDataCreated", "ShipmentStatus", dto.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_DuplicateCategoryName_ThrowsValidationException_CaseInsensitive()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);
        await sut.CreateAsync(MasterDataCollectionKey.Categories, new UpsertMasterDataRequest("Furniture", null, null), Actor);

        var act = async () => await sut.CreateAsync(MasterDataCollectionKey.Categories, new UpsertMasterDataRequest("furniture", null, null), Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task CreateAsync_DuplicateLookupCode_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);
        await sut.CreateAsync(MasterDataCollectionKey.VendorStatuses, new UpsertMasterDataRequest(null, "ACTIVE", "Active"), Actor);

        var act = async () => await sut.CreateAsync(MasterDataCollectionKey.VendorStatuses, new UpsertMasterDataRequest(null, "ACTIVE", "Different Label"), Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task CreateAsync_BlankName_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var act = async () => await sut.CreateAsync(MasterDataCollectionKey.Categories, new UpsertMasterDataRequest("   ", null, null), Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task CreateAsync_AssignsNextSortOrder()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);
        await sut.CreateAsync(MasterDataCollectionKey.LeadStatuses, new UpsertMasterDataRequest(null, "NEW", "New"), Actor);

        var second = await sut.CreateAsync(MasterDataCollectionKey.LeadStatuses, new UpsertMasterDataRequest(null, "QUALIFIED", "Qualified"), Actor);

        ((LookupItemDto)second).SortOrder.Should().Be(2);
    }

    // ---- Update: rename / edit label — code never changes -------------------

    [Fact]
    public async Task UpdateAsync_Category_Renames()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out var audit);
        var created = (CategoryDto)await sut.CreateAsync(MasterDataCollectionKey.Categories, new UpsertMasterDataRequest("Old Name", null, null), Actor);

        var updated = await sut.UpdateAsync(MasterDataCollectionKey.Categories, created.Id, new UpsertMasterDataRequest("New Name", null, null), Actor);

        ((CategoryDto)updated!).Name.Should().Be("New Name");
        audit.Verify(a => a.LogAsync(Actor, "MasterDataUpdated", "Category", created.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_Lookup_EditsLabelOnly_CodeIsImmutable()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);
        var created = (LookupItemDto)await sut.CreateAsync(MasterDataCollectionKey.InvoiceStatuses, new UpsertMasterDataRequest(null, "DRAFT", "Draft"), Actor);

        var updated = (LookupItemDto)(await sut.UpdateAsync(MasterDataCollectionKey.InvoiceStatuses, created.Id, new UpsertMasterDataRequest(null, "IGNORED-CODE", "Draft (Editable)"), Actor))!;

        updated.Code.Should().Be("DRAFT", "the request's Code field is intentionally ignored on update");
        updated.Label.Should().Be("Draft (Editable)");
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var result = await sut.UpdateAsync(MasterDataCollectionKey.Categories, Guid.NewGuid(), new UpsertMasterDataRequest("X", null, null), Actor);

        result.Should().BeNull();
    }

    // ---- Retire / Restore ----------------------------------------------------

    [Fact]
    public async Task RetireAsync_ThenRestoreAsync_TogglesIsActive()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out var audit);
        var created = (LookupItemDto)await sut.CreateAsync(MasterDataCollectionKey.VendorStatuses, new UpsertMasterDataRequest(null, "ON-HOLD", "On Hold"), Actor);

        var retired = (LookupItemDto)(await sut.RetireAsync(MasterDataCollectionKey.VendorStatuses, created.Id, Actor))!;
        retired.IsActive.Should().BeFalse();

        var restored = (LookupItemDto)(await sut.RestoreAsync(MasterDataCollectionKey.VendorStatuses, created.Id, Actor))!;
        restored.IsActive.Should().BeTrue();

        audit.Verify(a => a.LogAsync(Actor, "MasterDataRetired", "VendorStatus", created.Id.ToString(), null, default), Times.Once);
        audit.Verify(a => a.LogAsync(Actor, "MasterDataRestored", "VendorStatus", created.Id.ToString(), null, default), Times.Once);
    }

    [Fact]
    public async Task RetireAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var result = await sut.RetireAsync(MasterDataCollectionKey.ShipmentStatuses, Guid.NewGuid(), Actor);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RetiredRow_StillAppearsOnHistoricalRead_WhenIncludeRetiredIsTrue()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);
        var created = (LookupItemDto)await sut.CreateAsync(MasterDataCollectionKey.ShipmentStatuses, new UpsertMasterDataRequest(null, "PACKED", "Packed"), Actor);
        await sut.RetireAsync(MasterDataCollectionKey.ShipmentStatuses, created.Id, Actor);

        var activeOnly = await sut.GetAggregateAsync(includeRetired: false);
        var everything = await sut.GetAggregateAsync(includeRetired: true);

        activeOnly.ShipmentStatuses.Should().NotContain(s => s.Id == created.Id);
        everything.ShipmentStatuses.Should().ContainSingle(s => s.Id == created.Id && !s.IsActive);
    }

    // ---- Reorder --------------------------------------------------------------

    [Fact]
    public async Task ReorderAsync_AppliesNewSortOrders()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out var audit);
        var a = (LookupItemDto)await sut.CreateAsync(MasterDataCollectionKey.LeadStatuses, new UpsertMasterDataRequest(null, "NEW", "New"), Actor);
        var b = (LookupItemDto)await sut.CreateAsync(MasterDataCollectionKey.LeadStatuses, new UpsertMasterDataRequest(null, "WON", "Won"), Actor);

        var result = await sut.ReorderAsync(MasterDataCollectionKey.LeadStatuses,
            [new ReorderItemDto(b.Id, 1), new ReorderItemDto(a.Id, 2)], Actor);

        result.Cast<LookupItemDto>().Select(x => x.Code).Should().Equal("WON", "NEW");
        audit.Verify(a2 => a2.LogAsync(Actor, "MasterDataReordered", "LeadStatus", null, It.IsAny<object>(), default), Times.Once);
    }

    [Fact]
    public async Task ReorderAsync_WithUnknownId_ThrowsValidationException_AndAppliesNothing()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);
        var a = (LookupItemDto)await sut.CreateAsync(MasterDataCollectionKey.LeadStatuses, new UpsertMasterDataRequest(null, "NEW", "New"), Actor);

        var act = async () => await sut.ReorderAsync(MasterDataCollectionKey.LeadStatuses,
            [new ReorderItemDto(a.Id, 5), new ReorderItemDto(Guid.NewGuid(), 1)], Actor);

        await act.Should().ThrowAsync<AppValidationException>();
        db.LeadStatuses.Single(x => x.Id == a.Id).SortOrder.Should().Be(1, "the unknown id should have aborted the whole batch");
    }

    [Fact]
    public async Task ReorderAsync_EmptyList_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var act = async () => await sut.ReorderAsync(MasterDataCollectionKey.LeadStatuses, [], Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    // ---- Delete: referential safety (E3-08) ------------------------------------

    [Fact]
    public async Task DeleteAsync_UnreferencedRow_Succeeds()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out var audit);
        var created = (CategoryDto)await sut.CreateAsync(MasterDataCollectionKey.Categories, new UpsertMasterDataRequest("Unreferenced", null, null), Actor);

        var result = await sut.DeleteAsync(MasterDataCollectionKey.Categories, created.Id, Actor);

        result.Deleted.Should().BeTrue();
        db.Categories.Should().NotContain(c => c.Id == created.Id);
        audit.Verify(a => a.LogAsync(Actor, "MasterDataDeleted", "Category", created.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsNotFound()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var result = await sut.DeleteAsync(MasterDataCollectionKey.Categories, Guid.NewGuid(), Actor);

        result.NotFound.Should().BeTrue();
        result.Deleted.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_CategoryReferencedByCustomerCategory_ReturnsConflictNamingRetire()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);
        var category = (CategoryDto)await sut.CreateAsync(MasterDataCollectionKey.Categories, new UpsertMasterDataRequest("Referenced", null, null), Actor);

        var serviceType = new ServiceType { Id = Guid.NewGuid(), Code = "CIF", Label = "CIF", IsActive = true, SortOrder = 1 };
        var leadStatus = new LeadStatus { Id = Guid.NewGuid(), Code = "NEW", Label = "New", IsActive = true, SortOrder = 1 };
        db.ServiceTypes.Add(serviceType);
        db.LeadStatuses.Add(leadStatus);
        var customer = new Customer { Id = Guid.NewGuid(), Name = "Cust", Phone = "+911234567890", ServiceTypeId = serviceType.Id, StatusId = leadStatus.Id, CreatedAt = DateTime.UtcNow };
        db.Customers.Add(customer);
        db.CustomerCategories.Add(new CustomerCategory { CustomerId = customer.Id, CategoryId = category.Id });
        await db.SaveChangesAsync();

        var result = await sut.DeleteAsync(MasterDataCollectionKey.Categories, category.Id, Actor);

        result.Deleted.Should().BeFalse();
        result.NotFound.Should().BeFalse();
        result.ConflictDetail.Should().Contain("Retire");
        db.Categories.Should().Contain(c => c.Id == category.Id, "a referenced row must not be deleted");
    }

    [Fact]
    public async Task DeleteAsync_VendorStatusReferencedByVendor_ReturnsConflict()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);
        var status = (LookupItemDto)await sut.CreateAsync(MasterDataCollectionKey.VendorStatuses, new UpsertMasterDataRequest(null, "ACTIVE", "Active"), Actor);
        db.Vendors.Add(new Vendor { Id = Guid.NewGuid(), Name = "V1", StatusId = status.Id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await sut.DeleteAsync(MasterDataCollectionKey.VendorStatuses, status.Id, Actor);

        result.Deleted.Should().BeFalse();
        result.ConflictDetail.Should().NotBeNullOrWhiteSpace();
    }

    // ---- Delete: seeded system defaults are retire-only (N-8) ------------------------

    [Fact]
    public async Task DeleteAsync_SystemDefaultVendorStatus_ReturnsConflict_EvenThoughUnreferenced()
    {
        // Reproduces the exact defect from ACTION_PLAN §10.2 N-8: the M2 pass hard-deleted
        // the seeded "ACTIVE" vendor status because nothing referenced it yet.
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);
        db.VendorStatuses.Add(new VendorStatus { Id = Guid.NewGuid(), Code = "ACTIVE", Label = "Active", IsActive = true, SortOrder = 1, IsSystemDefault = true });
        await db.SaveChangesAsync();
        var seeded = db.VendorStatuses.Single(s => s.Code == "ACTIVE");

        var result = await sut.DeleteAsync(MasterDataCollectionKey.VendorStatuses, seeded.Id, Actor);

        result.Deleted.Should().BeFalse();
        result.NotFound.Should().BeFalse();
        result.ConflictDetail.Should().Contain("Retire");
        db.VendorStatuses.Should().Contain(s => s.Id == seeded.Id, "a seeded default must not be hard-deleted, even when nothing references it");
    }

    [Fact]
    public async Task DeleteAsync_SystemDefaultCategory_ReturnsConflict_EvenThoughUnreferenced()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);
        db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Jewellery", IsActive = true, SortOrder = 1, IsSystemDefault = true });
        await db.SaveChangesAsync();
        var seeded = db.Categories.Single(c => c.Name == "Jewellery");

        var result = await sut.DeleteAsync(MasterDataCollectionKey.Categories, seeded.Id, Actor);

        result.Deleted.Should().BeFalse();
        result.ConflictDetail.Should().Contain("Retire");
        db.Categories.Should().Contain(c => c.Id == seeded.Id);
    }

    [Fact]
    public async Task DeleteAsync_UserCreatedLookupRow_IsNotFlaggedSystemDefault_AndRemainsHardDeletable()
    {
        // The other half of N-8's requirement: user-added custom rows must keep working
        // exactly as built — hard-deletable while unreferenced.
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);
        var created = (LookupItemDto)await sut.CreateAsync(MasterDataCollectionKey.VendorStatuses, new UpsertMasterDataRequest(null, "CUSTOM", "Custom"), Actor);
        created.IsSystemDefault.Should().BeFalse("only DbSeeder-created rows are system defaults");

        var result = await sut.DeleteAsync(MasterDataCollectionKey.VendorStatuses, created.Id, Actor);

        result.Deleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_SystemDefaultRow_TakesPriorityOverReferenceCheck()
    {
        // "Regardless of reference count" per the coordinator's brief — prove the
        // system-default rejection still fires even when the row IS also referenced (not
        // just the more common unreferenced case above).
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);
        var status = new VendorStatus { Id = Guid.NewGuid(), Code = "ACTIVE", Label = "Active", IsActive = true, SortOrder = 1, IsSystemDefault = true };
        db.VendorStatuses.Add(status);
        db.Vendors.Add(new Vendor { Id = Guid.NewGuid(), Name = "V1", StatusId = status.Id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await sut.DeleteAsync(MasterDataCollectionKey.VendorStatuses, status.Id, Actor);

        result.Deleted.Should().BeFalse();
        result.ConflictDetail.Should().Contain("system default");
    }
}
