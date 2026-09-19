using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Moq;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Application.Inventory;
using SourcingOps.Application.Tests.TestSupport;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Application.Tests.Inventory;

/// <summary>
/// Covers ACTION_PLAN E7-01…E7-04. Weighted towards the failure/validation paths per the §7
/// DoD, and towards the three behaviours that are decisions rather than plumbing: the D-k
/// summary being computed over the whole filtered set rather than the page, the E7-04 stock
/// level classification matching the prototype's own <c>invRows()</c> boundaries exactly, and
/// UpdateAsync deliberately not being able to move stock.
/// </summary>
public class InventoryServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private static InventoryService CreateSut(AppDbContext db, out Mock<IAuditLogger> auditMock) =>
        CreateSut(db, out auditMock, out _);

    private static InventoryService CreateSut(AppDbContext db, out Mock<IAuditLogger> auditMock, out Mock<IFileStorage> storageMock)
    {
        auditMock = new Mock<IAuditLogger>();
        storageMock = new Mock<IFileStorage>();
        storageMock.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, CancellationToken _) => path);
        return new InventoryService(db, auditMock.Object, storageMock.Object);
    }

    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0];

    private sealed record Fixture(Category Jewellery, Category Tools, Vendor Vendor);

    private static Fixture SeedMasterData(AppDbContext db)
    {
        var jewellery = new Category { Id = Guid.NewGuid(), Name = "Jewellery", IsActive = true, SortOrder = 1 };
        var tools = new Category { Id = Guid.NewGuid(), Name = "Tools", IsActive = true, SortOrder = 6 };
        db.Categories.AddRange(jewellery, tools);

        var status = new VendorStatus { Id = Guid.NewGuid(), Code = "ACTIVE", Label = "Active", IsActive = true, SortOrder = 1 };
        db.VendorStatuses.Add(status);
        var vendor = new Vendor { Id = Guid.NewGuid(), Name = "Yiwu Jewel Craft Co.", StatusId = status.Id, CreatedAt = DateTime.UtcNow };
        db.Vendors.Add(vendor);

        db.Users.Add(new User { Id = Actor, Name = "Acting Staff", Email = $"actor-{Guid.NewGuid():N}@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });

        db.SaveChanges();
        return new Fixture(jewellery, tools, vendor);
    }

    private static InventoryItem AddItem(AppDbContext db, Fixture f, string name, decimal onHand, decimal reorder, decimal? unitCost = null, string? sku = null, Guid? categoryId = null, Guid? vendorId = null)
    {
        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            Name = name,
            Sku = sku,
            CategoryId = categoryId ?? f.Jewellery.Id,
            VendorId = vendorId,
            Unit = "pcs",
            OnHandQty = onHand,
            ReorderThreshold = reorder,
            UnitCost = unitCost
        };
        db.InventoryItems.Add(item);
        db.SaveChanges();
        return item;
    }

    private static CreateInventoryItemRequest ValidCreate(Fixture f, string name = "Imitation Kundan Set") =>
        new(name, "JWL-KUN-118", "Gold tone", f.Jewellery.Id, f.Vendor.Id, "set", 1840m, 600m, 500m);

    // ---- E7-04: stock level classification -------------------------------------------------

    // Boundaries taken from the approved prototype's invRows(): neg = qty < 0,
    // low = qty >= 0 && qty < reorder, else healthy. The reorder value itself is HEALTHY,
    // because the prototype's predicate is strictly `<`.
    [Theory]
    [InlineData(1840, 600, StockLevels.Healthy)]
    [InlineData(600, 600, StockLevels.Healthy)]   // exactly at threshold is NOT low
    [InlineData(599, 600, StockLevels.Low)]
    [InlineData(0, 600, StockLevels.Low)]         // zero is LOW, not NEGATIVE
    [InlineData(-40, 200, StockLevels.Negative)]
    [InlineData(0, 0, StockLevels.Healthy)]       // no threshold set: never low
    public void StockLevels_For_MatchesPrototypeBoundaries(int onHand, int reorder, string expected)
    {
        StockLevels.For(onHand, reorder).Should().Be(expected);
    }

    [Fact]
    public async Task GetAsync_ReturnsStockLevelAndComputedStockValue()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Ballpoint Pen Bulk Pack", onHand: -40m, reorder: 200m, unitCost: 12m);
        var sut = CreateSut(db, out _);

        var result = await sut.GetAsync(item.Id);

        result!.StockLevel.Should().Be(StockLevels.Negative);
        result.StockValue.Should().Be(-480m);
        result.OnHandQty.Should().Be(-40m, "the raw quantity is returned alongside the flag so the visual bar can do its own ratio maths");
        result.ReorderThreshold.Should().Be(200m);
    }

    [Fact]
    public async Task GetAsync_UncostedItem_ReturnsNullStockValue_NotZero()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Uncosted Item", onHand: 100m, reorder: 10m, unitCost: null);
        var sut = CreateSut(db, out _);

        var result = await sut.GetAsync(item.Id);

        result!.StockValue.Should().BeNull("null distinguishes 'not costed yet' from 'worth nothing'");
    }

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        (await sut.GetAsync(Guid.NewGuid())).Should().BeNull();
    }

    // ---- E7-01: create validation ------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_BlankName_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var act = () => sut.CreateAsync(ValidCreate(f) with { Name = "   " }, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("name");
    }

    [Fact]
    public async Task CreateAsync_UnknownCategory_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var act = () => sut.CreateAsync(ValidCreate(f) with { CategoryId = Guid.NewGuid() }, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("categoryId");
    }

    [Fact]
    public async Task CreateAsync_UnknownVendor_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var act = () => sut.CreateAsync(ValidCreate(f) with { VendorId = Guid.NewGuid() }, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("vendorId");
    }

    [Fact]
    public async Task CreateAsync_NegativeReorderThreshold_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var act = () => sut.CreateAsync(ValidCreate(f) with { ReorderThreshold = -1m }, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("reorderThreshold");
    }

    [Fact]
    public async Task CreateAsync_NegativeUnitCost_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var act = () => sut.CreateAsync(ValidCreate(f) with { UnitCost = -0.01m }, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("unitCost");
    }

    [Fact]
    public async Task CreateAsync_NullVendor_IsAllowed_SourceVendorIsOptional()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var result = await sut.CreateAsync(ValidCreate(f) with { VendorId = null }, Actor);

        result.Vendor.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_OmittedUnit_DefaultsToPcs()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var result = await sut.CreateAsync(ValidCreate(f) with { Unit = null }, Actor);

        result.Unit.Should().Be("pcs");
    }

    [Fact]
    public async Task CreateAsync_AuditLogsWithActorAndEntity()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit);

        var result = await sut.CreateAsync(ValidCreate(f), Actor);

        audit.Verify(a => a.LogAsync(Actor, "InventoryItemCreated", "InventoryItem", result.Id.ToString(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- E7-01: update ------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var result = await sut.UpdateAsync(Guid.NewGuid(),
            new UpdateInventoryItemRequest("Name", null, null, f.Jewellery.Id, null, null, 10m, null), Actor);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_CannotChangeOnHandQty_StockMovesOnlyThroughRecordedPaths()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Adjustable Wrench", onHand: 145m, reorder: 200m);
        var sut = CreateSut(db, out _);

        // OnHandQty omitted from the request — the balance must be left untouched.
        var result = await sut.UpdateAsync(item.Id,
            new UpdateInventoryItemRequest("Renamed Wrench", "TLS-WRN-999", "New description", f.Tools.Id, f.Vendor.Id, "box", 50m, 700m), Actor);

        result!.Name.Should().Be("Renamed Wrench");
        result.Category.Name.Should().Be("Tools");
        result.OnHandQty.Should().Be(145m, "an edit that doesn't send onHandQty must not rewrite the balance");
        result.StockLevel.Should().Be(StockLevels.Healthy, "145 is now above the lowered threshold of 50");
    }

    [Fact]
    public async Task UpdateAsync_BlankName_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Item", onHand: 1m, reorder: 1m);
        var sut = CreateSut(db, out _);

        var act = () => sut.UpdateAsync(item.Id, new UpdateInventoryItemRequest("", null, null, f.Jewellery.Id, null, null, 0m, null), Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("name");
    }

    // ---- E7-01: delete -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        (await sut.DeleteAsync(Guid.NewGuid(), Actor)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ItemReferencedByShipmentLine_ThrowsRatherThanFkViolation()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Referenced Item", onHand: 10m, reorder: 1m);

        var serviceType = new ServiceType { Id = Guid.NewGuid(), Code = "CIF", Label = "CIF", IsActive = true, SortOrder = 1 };
        var status = new ShipmentStatus { Id = Guid.NewGuid(), Code = "PACKED", Label = "Packed", IsActive = true, SortOrder = 1 };
        var leadStatus = new LeadStatus { Id = Guid.NewGuid(), Code = "ACTIVE", Label = "Active", IsActive = true, SortOrder = 1 };
        db.ServiceTypes.Add(serviceType);
        db.ShipmentStatuses.Add(status);
        db.LeadStatuses.Add(leadStatus);
        var customer = new Customer { Id = Guid.NewGuid(), Name = "Meena", Phone = "+911", ServiceTypeId = serviceType.Id, StatusId = leadStatus.Id, CreatedAt = DateTime.UtcNow };
        db.Customers.Add(customer);
        var shipment = new Shipment { Id = Guid.NewGuid(), CustomerId = customer.Id, ServiceTypeId = serviceType.Id, StatusId = status.Id, CreatedAt = DateTime.UtcNow };
        db.Shipments.Add(shipment);
        db.ShipmentLines.Add(new ShipmentLine { Id = Guid.NewGuid(), ShipmentId = shipment.Id, InventoryItemId = item.Id, Quantity = 5m });
        db.SaveChanges();

        var sut = CreateSut(db, out _);
        var act = () => sut.DeleteAsync(item.Id, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("id");
    }

    [Fact]
    public async Task DeleteAsync_UnreferencedItem_RemovesAndAuditLogs()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Disposable Item", onHand: 1m, reorder: 1m);
        var sut = CreateSut(db, out var audit);

        (await sut.DeleteAsync(item.Id, Actor)).Should().BeTrue();

        (await sut.GetAsync(item.Id)).Should().BeNull();
        audit.Verify(a => a.LogAsync(Actor, "InventoryItemDeleted", "InventoryItem", item.Id.ToString(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- E7-02: inbound stock (D-d) --------------------------------------------------------------

    [Fact]
    public async Task RecordInboundAsync_UnknownItem_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        (await sut.RecordInboundAsync(Guid.NewGuid(), new RecordInboundRequest(10m, null, null), Actor)).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task RecordInboundAsync_NonPositiveQuantity_Throws(int quantity)
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Item", onHand: 10m, reorder: 1m);
        var sut = CreateSut(db, out _);

        var act = () => sut.RecordInboundAsync(item.Id, new RecordInboundRequest(quantity, null, null), Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("quantity");
    }

    [Fact]
    public async Task RecordInboundAsync_RaisesOnHandAndWritesDurableEntry()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Bluetooth Earbuds", onHand: 260m, reorder: 400m, unitCost: 1400m);
        var sut = CreateSut(db, out var audit);

        var entryDate = new DateOnly(2026, 7, 21);
        var result = await sut.RecordInboundAsync(item.Id, new RecordInboundRequest(240m, entryDate, "GRN-4471"), Actor);

        result!.Item.OnHandQty.Should().Be(500m);
        result.Item.StockLevel.Should().Be(StockLevels.Healthy, "500 is now above the 400 threshold");
        result.Entry.Quantity.Should().Be(240m);
        result.Entry.EntryDate.Should().Be(entryDate);
        result.Entry.Reference.Should().Be("GRN-4471");
        result.Entry.RecordedByUserId.Should().Be(Actor);
        result.Entry.RecordedByName.Should().Be("Acting Staff");

        // The entry is a real row, not only an audit line — that is the whole point of D-d.
        var entries = await sut.ListInboundEntriesAsync(item.Id);
        entries!.Items.Should().ContainSingle().Which.Reference.Should().Be("GRN-4471");

        audit.Verify(a => a.LogAsync(Actor, "InventoryInboundRecorded", "InventoryItem", item.Id.ToString(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordInboundAsync_OmittedEntryDate_DefaultsToToday()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Item", onHand: 0m, reorder: 0m);
        var sut = CreateSut(db, out _);

        var result = await sut.RecordInboundAsync(item.Id, new RecordInboundRequest(5m, null, null), Actor);

        result!.Entry.EntryDate.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    [Fact]
    public async Task RecordInboundAsync_FromNegativeStock_CanClimbBackThroughZero()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Oversold Item", onHand: -40m, reorder: 200m);
        var sut = CreateSut(db, out _);

        var result = await sut.RecordInboundAsync(item.Id, new RecordInboundRequest(300m, null, null), Actor);

        result!.Item.OnHandQty.Should().Be(260m);
        result.Item.StockLevel.Should().Be(StockLevels.Healthy);
    }

    [Fact]
    public async Task ListInboundEntriesAsync_UnknownItem_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        (await sut.ListInboundEntriesAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task ListInboundEntriesAsync_ReturnsNewestFirst()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Item", onHand: 0m, reorder: 0m);
        var sut = CreateSut(db, out _);

        await sut.RecordInboundAsync(item.Id, new RecordInboundRequest(10m, new DateOnly(2026, 7, 1), "oldest"), Actor);
        await sut.RecordInboundAsync(item.Id, new RecordInboundRequest(10m, new DateOnly(2026, 7, 20), "newest"), Actor);
        await sut.RecordInboundAsync(item.Id, new RecordInboundRequest(10m, new DateOnly(2026, 7, 10), "middle"), Actor);

        var result = await sut.ListInboundEntriesAsync(item.Id);

        result!.Items.Select(e => e.Reference).Should().ContainInOrder("newest", "middle", "oldest");
    }

    // ---- N-38: stock adjustments (physical count corrections) -------------------------------

    [Fact]
    public async Task RecordAdjustmentAsync_UnknownItem_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        (await sut.RecordAdjustmentAsync(Guid.NewGuid(), new RecordAdjustmentRequest(10m, "Count", null), Actor)).Should().BeNull();
    }

    [Fact]
    public async Task RecordAdjustmentAsync_SetsOnHandAndRecordsPreviousAndDelta()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Item", onHand: 260m, reorder: 400m);
        var sut = CreateSut(db, out var audit);

        var adjustedOn = new DateOnly(2026, 7, 21);
        var result = await sut.RecordAdjustmentAsync(item.Id, new RecordAdjustmentRequest(300m, "Physical count found more stock", adjustedOn), Actor);

        result!.Item.OnHandQty.Should().Be(300m);
        result.Adjustment.CountedQty.Should().Be(300m);
        result.Adjustment.PreviousQty.Should().Be(260m);
        result.Adjustment.Delta.Should().Be(40m);
        result.Adjustment.Reason.Should().Be("Physical count found more stock");
        result.Adjustment.AdjustedOn.Should().Be(adjustedOn);
        result.Adjustment.AdjustedByUserId.Should().Be(Actor);
        result.Adjustment.AdjustedByName.Should().Be("Acting Staff");

        audit.Verify(a => a.LogAsync(Actor, "InventoryStockAdjustmentRecorded", "InventoryItem", item.Id.ToString(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordAdjustmentAsync_AdjustingDown_SetsOnHandAndNegativeDelta()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Item", onHand: 260m, reorder: 400m);
        var sut = CreateSut(db, out _);

        var result = await sut.RecordAdjustmentAsync(item.Id, new RecordAdjustmentRequest(200m, "Shrinkage", null), Actor);

        result!.Item.OnHandQty.Should().Be(200m);
        result.Adjustment.PreviousQty.Should().Be(260m);
        result.Adjustment.Delta.Should().Be(-60m);
    }

    [Fact]
    public async Task RecordAdjustmentAsync_FromNegativeStock_CanCountUpToPositive()
    {
        // D-35: existing OnHandQty can be negative (oversold). The counted VALUE cannot be
        // negative, but adjusting FROM a negative on-hand TO a positive count is legitimate.
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Oversold Item", onHand: -40m, reorder: 200m);
        var sut = CreateSut(db, out _);

        var result = await sut.RecordAdjustmentAsync(item.Id, new RecordAdjustmentRequest(15m, "Physical recount", null), Actor);

        result!.Item.OnHandQty.Should().Be(15m);
        result.Adjustment.PreviousQty.Should().Be(-40m);
        result.Adjustment.Delta.Should().Be(55m);
    }

    [Fact]
    public async Task RecordAdjustmentAsync_ZeroDelta_IsStillRecorded()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Item", onHand: 100m, reorder: 10m);
        var sut = CreateSut(db, out _);

        var result = await sut.RecordAdjustmentAsync(item.Id, new RecordAdjustmentRequest(100m, "Count matched exactly", null), Actor);

        result!.Adjustment.Delta.Should().Be(0m);
        result.Item.OnHandQty.Should().Be(100m);

        var history = await sut.ListStockAdjustmentsAsync(item.Id);
        history!.Items.Should().ContainSingle("a no-op adjustment is still a meaningful audit fact");
    }

    [Fact]
    public async Task RecordAdjustmentAsync_NegativeCountedQty_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Item", onHand: 10m, reorder: 1m);
        var sut = CreateSut(db, out _);

        var act = () => sut.RecordAdjustmentAsync(item.Id, new RecordAdjustmentRequest(-1m, "Bad count", null), Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("countedQty");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RecordAdjustmentAsync_BlankReason_Throws(string? reason)
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Item", onHand: 10m, reorder: 1m);
        var sut = CreateSut(db, out _);

        var act = () => sut.RecordAdjustmentAsync(item.Id, new RecordAdjustmentRequest(10m, reason!, null), Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("reason");
    }

    [Fact]
    public async Task RecordAdjustmentAsync_OmittedAdjustedOn_DefaultsToToday()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Item", onHand: 0m, reorder: 0m);
        var sut = CreateSut(db, out _);

        var result = await sut.RecordAdjustmentAsync(item.Id, new RecordAdjustmentRequest(5m, "Opening count", null), Actor);

        result!.Adjustment.AdjustedOn.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    [Fact]
    public async Task ListStockAdjustmentsAsync_UnknownItem_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        (await sut.ListStockAdjustmentsAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task ListStockAdjustmentsAsync_ReturnsNewestFirst()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Item", onHand: 0m, reorder: 0m);
        var sut = CreateSut(db, out _);

        await sut.RecordAdjustmentAsync(item.Id, new RecordAdjustmentRequest(10m, "oldest", new DateOnly(2026, 7, 1)), Actor);
        await sut.RecordAdjustmentAsync(item.Id, new RecordAdjustmentRequest(10m, "newest", new DateOnly(2026, 7, 20)), Actor);
        await sut.RecordAdjustmentAsync(item.Id, new RecordAdjustmentRequest(10m, "middle", new DateOnly(2026, 7, 10)), Actor);

        var result = await sut.ListStockAdjustmentsAsync(item.Id);

        result!.Items.Select(e => e.Reason).Should().ContainInOrder("newest", "middle", "oldest");
    }

    // ---- E7-03: list, filters and the D-k summary --------------------------------------------------

    private static void SeedListScenario(AppDbContext db, Fixture f)
    {
        // 1840 @ 500 = 920,000 healthy
        AddItem(db, f, "Imitation Kundan Set", onHand: 1840m, reorder: 600m, unitCost: 500m, sku: "JWL-KUN-118", vendorId: f.Vendor.Id);
        // 260 @ 1400 = 364,000 low
        AddItem(db, f, "Bluetooth Earbuds TWS-9", onHand: 260m, reorder: 400m, unitCost: 1400m, sku: "ELE-TWS-009");
        // -40 @ 0 = 0 negative
        AddItem(db, f, "Ballpoint Pen Bulk Pack", onHand: -40m, reorder: 200m, unitCost: 0m, sku: "STN-BPP-050");
        // 145 @ 700 = 101,500 low, different category
        AddItem(db, f, "Adjustable Wrench 10in", onHand: 145m, reorder: 200m, unitCost: 700m, sku: "TLS-WRN-310", categoryId: f.Tools.Id);
    }

    [Fact]
    public async Task ListAsync_SummaryIsComputedOverWholeFilteredSet_NotThePage()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        SeedListScenario(db, f);
        var sut = CreateSut(db, out _);

        var result = await sut.ListAsync(new InventoryListQuery(null, Page: 1, PageSize: 1, null, null, null));

        result.Items.Should().HaveCount(1, "page size is 1");
        result.TotalCount.Should().Be(4);
        result.Summary.ItemCount.Should().Be(4, "the summary must ignore paging entirely");
        result.Summary.OnHandValue.Should().Be(920_000m + 364_000m + 0m + 101_500m);
        result.Summary.LowStockCount.Should().Be(2, "earbuds and wrench are below reorder but not negative");
        result.Summary.NegativeStockCount.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_SummaryHonoursActiveFilters()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        SeedListScenario(db, f);
        var sut = CreateSut(db, out _);

        var result = await sut.ListAsync(new InventoryListQuery(null, 1, 25, CategoryId: f.Tools.Id, null, null));

        result.Summary.ItemCount.Should().Be(1);
        result.Summary.OnHandValue.Should().Be(101_500m);
        result.Summary.LowStockCount.Should().Be(1);
        result.Summary.NegativeStockCount.Should().Be(0);
    }

    [Fact]
    public async Task ListAsync_UncostedItemsContributeZeroToOnHandValue_RatherThanNullingIt()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        AddItem(db, f, "Costed", onHand: 10m, reorder: 1m, unitCost: 100m);
        AddItem(db, f, "Uncosted", onHand: 10m, reorder: 1m, unitCost: null);
        var sut = CreateSut(db, out _);

        var result = await sut.ListAsync(new InventoryListQuery(null, 1, 25, null, null, null));

        result.Summary.OnHandValue.Should().Be(1000m);
    }

    [Fact]
    public async Task ListAsync_LowFilter_IncludesNegativeStock_MatchingThePrototypesSingleOption()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        SeedListScenario(db, f);
        var sut = CreateSut(db, out _);

        var result = await sut.ListAsync(new InventoryListQuery(null, 1, 25, null, null, StockLevelFilters.Low));

        result.Items.Select(i => i.StockLevel).Should().BeEquivalentTo([StockLevels.Low, StockLevels.Low, StockLevels.Negative]);
    }

    [Fact]
    public async Task ListAsync_HealthyFilter_ExcludesLowAndNegative()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        SeedListScenario(db, f);
        var sut = CreateSut(db, out _);

        var result = await sut.ListAsync(new InventoryListQuery(null, 1, 25, null, null, StockLevelFilters.Healthy));

        result.Items.Should().ContainSingle().Which.Name.Should().Be("Imitation Kundan Set");
    }

    [Fact]
    public async Task ListAsync_UnknownStockLevelFilter_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var act = () => sut.ListAsync(new InventoryListQuery(null, 1, 25, null, null, "urgent"));

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("stockLevel");
    }

    [Fact]
    public async Task ListAsync_SearchMatchesNameAndSku()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        SeedListScenario(db, f);
        var sut = CreateSut(db, out _);

        var byName = await sut.ListAsync(new InventoryListQuery("earbuds", 1, 25, null, null, null));
        var bySku = await sut.ListAsync(new InventoryListQuery("TLS-WRN", 1, 25, null, null, null));

        byName.Items.Should().ContainSingle().Which.Sku.Should().Be("ELE-TWS-009");
        bySku.Items.Should().ContainSingle().Which.Name.Should().Be("Adjustable Wrench 10in");
    }

    [Fact]
    public async Task ListAsync_VendorFilter_NarrowsToThatVendor()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        SeedListScenario(db, f);
        var sut = CreateSut(db, out _);

        var result = await sut.ListAsync(new InventoryListQuery(null, 1, 25, null, VendorId: f.Vendor.Id, null));

        result.Items.Should().ContainSingle().Which.Vendor!.Name.Should().Be("Yiwu Jewel Craft Co.");
    }

    [Fact]
    public async Task ListAsync_OutOfRangePagingIsCoerced_NotRejected()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        SeedListScenario(db, f);
        var sut = CreateSut(db, out _);

        var result = await sut.ListAsync(new InventoryListQuery(null, Page: 0, PageSize: 5000, null, null, null));

        result.Page.Should().Be(1);
        result.PageSize.Should().Be(25);
    }

    // ---- Edited count (onHandQty on update) ------------------------------------------------

    [Fact]
    public async Task UpdateAsync_WithChangedOnHandQty_SetsBalanceAndRecordsAdjustment()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Ring", onHand: 40m, reorder: 10m);
        var sut = CreateSut(db, out _);

        var result = await sut.UpdateAsync(item.Id,
            new UpdateInventoryItemRequest("Ring", null, null, f.Jewellery.Id, null, "pcs", 10m, null, OnHandQty: 25m), Actor);

        result!.OnHandQty.Should().Be(25m);
        var adjustment = await db.InventoryStockAdjustments.SingleAsync(a => a.InventoryItemId == item.Id);
        adjustment.PreviousQty.Should().Be(40m);
        adjustment.CountedQty.Should().Be(25m);
        adjustment.Delta.Should().Be(-15m);
        adjustment.Reason.Should().Be(InventoryService.CountEditedReason);
    }

    [Fact]
    public async Task UpdateAsync_WithUnchangedOnHandQty_RecordsNoAdjustment()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Ring", onHand: 40m, reorder: 10m);
        var sut = CreateSut(db, out _);

        await sut.UpdateAsync(item.Id,
            new UpdateInventoryItemRequest("Ring", null, null, f.Jewellery.Id, null, "pcs", 10m, null, OnHandQty: 40m), Actor);

        (await db.InventoryStockAdjustments.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_NegativeOnHandQty_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Ring", onHand: 40m, reorder: 10m);
        var sut = CreateSut(db, out _);

        var act = () => sut.UpdateAsync(item.Id,
            new UpdateInventoryItemRequest("Ring", null, null, f.Jewellery.Id, null, "pcs", 10m, null, OnHandQty: -1m), Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("onHandQty");
    }

    // ---- Images -----------------------------------------------------------------------------

    [Fact]
    public async Task SetImageAsync_StoresImageAndReturnsInlineThumbnail()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Ring", onHand: 1m, reorder: 0m);
        var sut = CreateSut(db, out _, out var storage);

        var result = await sut.SetImageAsync(item.Id, new MemoryStream(PngBytes), PngBytes.Length, JpegBytes, Actor);

        result!.HasImage.Should().BeTrue();
        result.ThumbnailDataUrl.Should().StartWith("data:image/jpeg;base64,");
        storage.Verify(s => s.SaveAsync(It.Is<string>(p => p.StartsWith($"inventory-images/{item.Id}/") && p.EndsWith(".png")),
            It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        (await db.InventoryItems.SingleAsync(i => i.Id == item.Id)).ImageContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task SetImageAsync_ReplacingImage_DeletesPreviousFile()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Ring", onHand: 1m, reorder: 0m);
        var sut = CreateSut(db, out _, out var storage);

        await sut.SetImageAsync(item.Id, new MemoryStream(PngBytes), PngBytes.Length, JpegBytes, Actor);
        var firstPath = (await db.InventoryItems.SingleAsync(i => i.Id == item.Id)).ImagePath!;
        await sut.SetImageAsync(item.Id, new MemoryStream(JpegBytes), JpegBytes.Length, JpegBytes, Actor);

        storage.Verify(s => s.DeleteAsync(firstPath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetImageAsync_NonImageBytes_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Ring", onHand: 1m, reorder: 0m);
        var sut = CreateSut(db, out _);
        var pdf = "%PDF-1.7 fake"u8.ToArray();

        var act = () => sut.SetImageAsync(item.Id, new MemoryStream(pdf), pdf.Length, JpegBytes, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("file");
    }

    [Fact]
    public async Task RemoveImageAsync_ClearsImageAndDeletesFile()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = AddItem(db, f, "Ring", onHand: 1m, reorder: 0m);
        var sut = CreateSut(db, out _, out var storage);
        await sut.SetImageAsync(item.Id, new MemoryStream(PngBytes), PngBytes.Length, JpegBytes, Actor);
        var path = (await db.InventoryItems.SingleAsync(i => i.Id == item.Id)).ImagePath!;

        var result = await sut.RemoveImageAsync(item.Id, Actor);

        result!.HasImage.Should().BeFalse();
        result.ThumbnailDataUrl.Should().BeNull();
        storage.Verify(s => s.DeleteAsync(path, It.IsAny<CancellationToken>()), Times.Once);
    }
}
