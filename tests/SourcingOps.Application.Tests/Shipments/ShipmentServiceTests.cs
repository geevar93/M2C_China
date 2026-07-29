using FluentAssertions;
using Moq;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Application.Shipments;
using SourcingOps.Application.Tests.TestSupport;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Application.Tests.Shipments;

/// <summary>
/// Covers ACTION_PLAN E7-05…E7-08 and E7-10. Weighted towards the failure/validation paths per
/// the §7 DoD and towards the settled decisions that are easy to regress silently: the
/// negative-stock guard and its explicit override (D-g), delta-based stock movement on edit
/// (D-j), server-computed totals (D-c), the freight-only line ban keyed on Code (D-h), and
/// status history being written for every transition including creation (D-e).
///
/// Reference generation's COLLISION-retry path (D-i) is deliberately not asserted here: the EF
/// Core InMemory provider does not enforce unique indexes, so a collision cannot be produced.
/// The format is asserted; the retry is covered by the integration suite and the live run.
/// </summary>
public class ShipmentServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private static ShipmentService CreateSut(AppDbContext db, out Mock<IAuditLogger> auditMock)
    {
        auditMock = new Mock<IAuditLogger>();
        return new ShipmentService(db, auditMock.Object);
    }

    private sealed record Fixture(
        Customer Customer,
        Customer OtherCustomer,
        ServiceType Cif,
        ServiceType FreightOnly,
        ShipmentStatus Packed,
        ShipmentStatus Dispatched,
        ShipmentStatus InTransit,
        InventoryItem Kundan,
        InventoryItem Earbuds);

    private static Fixture SeedMasterData(AppDbContext db)
    {
        var cif = new ServiceType { Id = Guid.NewGuid(), Code = "CIF", Label = "CIF", IsActive = true, SortOrder = 1 };
        var freightOnly = new ServiceType { Id = Guid.NewGuid(), Code = "FREIGHT_ONLY", Label = "Freight-only", IsActive = true, SortOrder = 2 };
        db.ServiceTypes.AddRange(cif, freightOnly);

        var packed = new ShipmentStatus { Id = Guid.NewGuid(), Code = "PACKED", Label = "Packed", IsActive = true, SortOrder = 1 };
        var dispatched = new ShipmentStatus { Id = Guid.NewGuid(), Code = "DISPATCHED", Label = "Dispatched", IsActive = true, SortOrder = 2 };
        // "IN TRANSIT" with a literal space — the seeded code the frontend's colour map keys off.
        var inTransit = new ShipmentStatus { Id = Guid.NewGuid(), Code = "IN TRANSIT", Label = "In Transit", IsActive = true, SortOrder = 3 };
        db.ShipmentStatuses.AddRange(packed, dispatched, inTransit);

        var leadStatus = new LeadStatus { Id = Guid.NewGuid(), Code = "ACTIVE", Label = "Active", IsActive = true, SortOrder = 1 };
        db.LeadStatuses.Add(leadStatus);

        var customer = new Customer { Id = Guid.NewGuid(), Name = "Meena Patel", BusinessName = "Meena Traders", Phone = "+919000000001", ServiceTypeId = cif.Id, StatusId = leadStatus.Id, CreatedAt = DateTime.UtcNow };
        // No BusinessName — proves the CustomerRefDto fallback to the contact name.
        var otherCustomer = new Customer { Id = Guid.NewGuid(), Name = "Suresh Kumar", BusinessName = null, Phone = "+919000000002", ServiceTypeId = cif.Id, StatusId = leadStatus.Id, CreatedAt = DateTime.UtcNow };
        db.Customers.AddRange(customer, otherCustomer);

        var category = new Category { Id = Guid.NewGuid(), Name = "Jewellery", IsActive = true, SortOrder = 1 };
        db.Categories.Add(category);

        var kundan = new InventoryItem { Id = Guid.NewGuid(), Name = "Imitation Kundan Set", Sku = "JWL-KUN-118", CategoryId = category.Id, Unit = "set", OnHandQty = 100m, ReorderThreshold = 20m, UnitCost = 500m };
        var earbuds = new InventoryItem { Id = Guid.NewGuid(), Name = "Bluetooth Earbuds", Sku = "ELE-TWS-009", CategoryId = category.Id, Unit = "unit", OnHandQty = 50m, ReorderThreshold = 10m, UnitCost = 1400m };
        db.InventoryItems.AddRange(kundan, earbuds);

        db.Users.Add(new User { Id = Actor, Name = "Vikram Nair", Email = $"actor-{Guid.NewGuid():N}@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });

        db.SaveChanges();
        return new Fixture(customer, otherCustomer, cif, freightOnly, packed, dispatched, inTransit, kundan, earbuds);
    }

    private static CreateShipmentRequest ValidCreate(Fixture f, IReadOnlyList<ShipmentLineRequest>? lines = null) =>
        new(f.Customer.Id, "Surat, Gujarat", f.Cif.Id, new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc), f.Packed.Id,
            FreightCost: 48_000m, TotalValue: null, Mode: "Sea LCL · Nhava Sheva", AwbOrBl: "BL SNKO4471192",
            Eta: new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc), Lines: lines);

    private static decimal OnHand(AppDbContext db, Guid itemId) => db.InventoryItems.Single(i => i.Id == itemId).OnHandQty;

    // ---- E7-05: create validation --------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_UnknownCustomer_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var act = () => sut.CreateAsync(ValidCreate(f) with { CustomerId = Guid.NewGuid() }, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("customerId");
    }

    [Fact]
    public async Task CreateAsync_UnknownServiceType_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var act = () => sut.CreateAsync(ValidCreate(f) with { ServiceTypeId = Guid.NewGuid() }, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("serviceTypeId");
    }

    [Fact]
    public async Task CreateAsync_UnknownStatus_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var act = () => sut.CreateAsync(ValidCreate(f) with { StatusId = Guid.NewGuid() }, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("statusId");
    }

    [Fact]
    public async Task CreateAsync_UnknownInventoryItemOnALine_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var act = () => sut.CreateAsync(ValidCreate(f, [new ShipmentLineRequest(Guid.NewGuid(), 1m, null)]), Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("lines");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task CreateAsync_NonPositiveLineQuantity_Throws(int quantity)
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var act = () => sut.CreateAsync(ValidCreate(f, [new ShipmentLineRequest(f.Kundan.Id, quantity, null)]), Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("lines[0].quantity");
    }

    [Fact]
    public async Task CreateAsync_ReportsEveryBadLineAtOnce_NotJustTheFirst()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var act = () => sut.CreateAsync(ValidCreate(f, [
            new ShipmentLineRequest(f.Kundan.Id, 0m, null),
            new ShipmentLineRequest(f.Earbuds.Id, -1m, null)
        ]), Actor);

        var errors = (await act.Should().ThrowAsync<AppValidationException>()).And.Errors;
        errors.Should().ContainKeys("lines[0].quantity", "lines[1].quantity");
    }

    // ---- E7-06 / D-g: negative stock guard and override -------------------------------------------

    [Fact]
    public async Task CreateAsync_DecrementsStockInTheSameSave()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        await sut.CreateAsync(ValidCreate(f, [new ShipmentLineRequest(f.Kundan.Id, 30m, null)]), Actor);

        OnHand(db, f.Kundan.Id).Should().Be(70m);
    }

    [Fact]
    public async Task CreateAsync_WouldDriveStockNegative_Throws409PayloadAndMovesNothing()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var act = () => sut.CreateAsync(ValidCreate(f, [new ShipmentLineRequest(f.Kundan.Id, 140m, null)]), Actor);

        var ex = (await act.Should().ThrowAsync<InsufficientStockException>()).And;
        var detail = ex.Items.Should().ContainSingle().Which;
        detail.InventoryItemId.Should().Be(f.Kundan.Id);
        detail.ItemName.Should().Be("Imitation Kundan Set");
        detail.Sku.Should().Be("JWL-KUN-118");
        detail.RequestedQty.Should().Be(140m);
        detail.AvailableQty.Should().Be(100m);

        OnHand(db, f.Kundan.Id).Should().Be(100m, "nothing is committed when the guard trips");
        db.Shipments.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_CollectsEveryShortfall_NotJustTheFirst()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var act = () => sut.CreateAsync(ValidCreate(f, [
            new ShipmentLineRequest(f.Kundan.Id, 500m, null),
            new ShipmentLineRequest(f.Earbuds.Id, 500m, null)
        ]), Actor);

        var ex = (await act.Should().ThrowAsync<InsufficientStockException>()).And;
        ex.Items.Should().HaveCount(2, "a caller fixing a multi-line shipment should see every shortfall at once");
    }

    [Fact]
    public async Task CreateAsync_AllowNegativeStock_PermitsTheDecrementAndFlagsTheOverride()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit);

        var result = await sut.CreateAsync(
            ValidCreate(f, [new ShipmentLineRequest(f.Kundan.Id, 140m, null)]) with { AllowNegativeStock = true }, Actor);

        result.Should().NotBeNull();
        OnHand(db, f.Kundan.Id).Should().Be(-40m, "the prototype's own seed data reaches qty: -40, so this must be reachable");

        audit.Verify(a => a.LogAsync(Actor, "ShipmentCreated", "Shipment", It.IsAny<string>(),
            It.Is<object>(o => o.ToString()!.Contains("NegativeStockOverride = True")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_LinesSummedPerItem_BeforeTheGuardIsApplied()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        // Two lines of 60 against the same item with 100 on hand: neither alone overdraws, the
        // pair does. Guarding per line rather than per item would let this through.
        var act = () => sut.CreateAsync(ValidCreate(f, [
            new ShipmentLineRequest(f.Kundan.Id, 60m, null),
            new ShipmentLineRequest(f.Kundan.Id, 60m, null)
        ]), Actor);

        var ex = (await act.Should().ThrowAsync<InsufficientStockException>()).And;
        ex.Items.Should().ContainSingle().Which.RequestedQty.Should().Be(120m);
    }

    // ---- E7-10 / D-h: freight-only ------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_FreightOnlyWithLines_ThrowsAndNamesTheLines()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var act = () => sut.CreateAsync(
            ValidCreate(f, [new ShipmentLineRequest(f.Kundan.Id, 1m, null)]) with { ServiceTypeId = f.FreightOnly.Id }, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("lines");
    }

    [Fact]
    public async Task CreateAsync_FreightOnlyWithoutLines_MovesNoStock()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var result = await sut.CreateAsync(
            ValidCreate(f) with { ServiceTypeId = f.FreightOnly.Id, TotalValue = 64_500m }, Actor);

        result.Lines.Should().BeEmpty();
        result.ServiceType.Code.Should().Be("FREIGHT_ONLY");
        OnHand(db, f.Kundan.Id).Should().Be(100m);
        OnHand(db, f.Earbuds.Id).Should().Be(50m);
    }

    [Fact]
    public async Task CreateAsync_CifWithZeroLines_IsAllowed()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var result = await sut.CreateAsync(ValidCreate(f), Actor);

        result.Lines.Should().BeEmpty("E7-10 says a CIF shipment may have zero or more lines");
    }

    // ---- D-b / D-c: unit cost snapshot and server-computed total ---------------------------------------

    [Fact]
    public async Task CreateAsync_LineUnitCostDefaultsFromTheItem_AndTotalIsComputed()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var result = await sut.CreateAsync(ValidCreate(f, [
            new ShipmentLineRequest(f.Kundan.Id, 10m, null),      // 10 x 500 = 5,000
            new ShipmentLineRequest(f.Earbuds.Id, 2m, null)       // 2 x 1400 = 2,800
        ]), Actor);

        result.Lines.Single(l => l.InventoryItemId == f.Kundan.Id).UnitCost.Should().Be(500m);
        result.Lines.Single(l => l.InventoryItemId == f.Kundan.Id).LineTotal.Should().Be(5_000m);
        result.TotalValue.Should().Be(7_800m);
    }

    [Fact]
    public async Task CreateAsync_SuppliedLineUnitCostWins_OverTheItemsCurrentCost()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var result = await sut.CreateAsync(ValidCreate(f, [new ShipmentLineRequest(f.Kundan.Id, 10m, 450m)]), Actor);

        result.Lines.Should().ContainSingle().Which.UnitCost.Should().Be(450m);
        result.TotalValue.Should().Be(4_500m);
    }

    [Fact]
    public async Task CreateAsync_ClientTotalIsIgnoredWhenLinesExist_ButHonouredWhenTheyDoNot()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var withLines = await sut.CreateAsync(
            ValidCreate(f, [new ShipmentLineRequest(f.Kundan.Id, 10m, null)]) with { TotalValue = 999_999m }, Actor);
        var withoutLines = await sut.CreateAsync(ValidCreate(f) with { TotalValue = 64_500m }, Actor);

        withLines.TotalValue.Should().Be(5_000m, "a client total may never contradict the lines");
        withoutLines.TotalValue.Should().Be(64_500m, "with no lines there is nothing to contradict");
    }

    [Fact]
    public async Task CreateAsync_ItemUnitCostChangeLater_DoesNotRewriteTheShipmentsSnapshot()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCreate(f, [new ShipmentLineRequest(f.Kundan.Id, 10m, null)]), Actor);

        db.InventoryItems.Single(i => i.Id == f.Kundan.Id).UnitCost = 9_999m;
        await db.SaveChangesAsync();

        var reread = await sut.GetAsync(created.Id);

        reread!.Lines.Should().ContainSingle().Which.UnitCost.Should().Be(500m,
            "E8 will raise CIF invoices against this shipment; a live read would rewrite an issued invoice's basis");
        reread.TotalValue.Should().Be(5_000m);
    }

    // ---- D-i: reference generation ----------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_GeneratesSequentialMonthlyReference_AndIgnoresAnyClientValue()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var first = await sut.CreateAsync(ValidCreate(f), Actor);
        var second = await sut.CreateAsync(ValidCreate(f), Actor);

        var expectedPrefix = $"SHP-{DateTime.UtcNow:yyMM}-";
        first.Reference.Should().Be(expectedPrefix + "001");
        second.Reference.Should().Be(expectedPrefix + "002");
        // CreateShipmentRequest has no Reference member at all, so a client cannot supply one.
        typeof(CreateShipmentRequest).GetProperty("Reference").Should().BeNull();
    }

    // ---- E7-07 / D-e: status transitions and history -------------------------------------------------------

    [Fact]
    public async Task CreateAsync_SeedsAnOpeningStatusHistoryRow()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var result = await sut.CreateAsync(ValidCreate(f), Actor);

        var opening = result.StatusHistory.Should().ContainSingle().Which;
        opening.Status.Code.Should().Be("PACKED");
        opening.ChangedByUserId.Should().Be(Actor);
        opening.ChangedByName.Should().Be("Vikram Nair");
        result.RecordedByName.Should().Be("Vikram Nair", "the detail screen's 'Recorded by' is read from the earliest history row");
    }

    [Fact]
    public async Task ChangeStatusAsync_AppendsHistoryInChronologicalOrder_AndAuditLogs()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);

        await sut.ChangeStatusAsync(created.Id, new ChangeShipmentStatusRequest(f.Dispatched.Id, "Handed to carrier"), Actor);
        var result = await sut.ChangeStatusAsync(created.Id, new ChangeShipmentStatusRequest(f.InTransit.Id, null), Actor);

        result!.Status.Code.Should().Be("IN TRANSIT");
        result.StatusHistory.Select(h => h.Status.Code).Should().ContainInOrder("PACKED", "DISPATCHED", "IN TRANSIT");
        result.StatusHistory.Select(h => h.ChangedAt).Should().BeInAscendingOrder();
        result.StatusHistory[1].Note.Should().Be("Handed to carrier");

        // BOTH surfaces are written — E7-07 requires audit-logged AND timestamped.
        audit.Verify(a => a.LogAsync(Actor, "ShipmentStatusChanged", "Shipment", created.Id.ToString(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ChangeStatusAsync_ToTheStatusAlreadyHeld_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);

        var act = () => sut.ChangeStatusAsync(created.Id, new ChangeShipmentStatusRequest(f.Packed.Id, null), Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("statusId");
    }

    [Fact]
    public async Task ChangeStatusAsync_BackwardsTransitionIsAllowed_TheLookupIsUserConfigurable()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);
        await sut.ChangeStatusAsync(created.Id, new ChangeShipmentStatusRequest(f.InTransit.Id, null), Actor);

        // A hard-coded legal-transition graph would break the moment the business adds a stage,
        // so only the no-op is rejected — going back to PACKED must succeed.
        var result = await sut.ChangeStatusAsync(created.Id, new ChangeShipmentStatusRequest(f.Packed.Id, "Recalled"), Actor);

        result!.Status.Code.Should().Be("PACKED");
    }

    [Fact]
    public async Task ChangeStatusAsync_NeverMovesStock()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCreate(f, [new ShipmentLineRequest(f.Kundan.Id, 30m, null)]), Actor);

        await sut.ChangeStatusAsync(created.Id, new ChangeShipmentStatusRequest(f.InTransit.Id, null), Actor);

        OnHand(db, f.Kundan.Id).Should().Be(70m, "there is no Cancelled status in the seeded set, so transitions never restore stock");
    }

    [Fact]
    public async Task ChangeStatusAsync_UnknownShipment_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        (await sut.ChangeStatusAsync(Guid.NewGuid(), new ChangeShipmentStatusRequest(f.Packed.Id, null), Actor)).Should().BeNull();
    }

    [Fact]
    public async Task ChangeStatusAsync_UnknownStatus_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);

        var act = () => sut.ChangeStatusAsync(created.Id, new ChangeShipmentStatusRequest(Guid.NewGuid(), null), Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("statusId");
    }

    // ---- D-j: delta-based stock movement on update ----------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_RaisingALineQuantity_ConsumesOnlyTheDifference()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCreate(f, [new ShipmentLineRequest(f.Kundan.Id, 30m, null)]), Actor);
        OnHand(db, f.Kundan.Id).Should().Be(70m);

        await sut.UpdateAsync(created.Id, Update(f, [new ShipmentLineRequest(f.Kundan.Id, 50m, null)]), Actor);

        OnHand(db, f.Kundan.Id).Should().Be(50m, "only the extra 20 is consumed, not a fresh 50 off 70");
    }

    [Fact]
    public async Task UpdateAsync_LoweringALineQuantity_RestoresTheDifference()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCreate(f, [new ShipmentLineRequest(f.Kundan.Id, 30m, null)]), Actor);

        await sut.UpdateAsync(created.Id, Update(f, [new ShipmentLineRequest(f.Kundan.Id, 10m, null)]), Actor);

        OnHand(db, f.Kundan.Id).Should().Be(90m);
    }

    [Fact]
    public async Task UpdateAsync_RemovingALine_RestoresItsFullQuantity()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCreate(f, [
            new ShipmentLineRequest(f.Kundan.Id, 30m, null),
            new ShipmentLineRequest(f.Earbuds.Id, 5m, null)
        ]), Actor);

        // Earbuds line dropped entirely — the request never mentions that item again, so the
        // restore has to come from the EXISTING lines, not from the incoming ones.
        var result = await sut.UpdateAsync(created.Id, Update(f, [new ShipmentLineRequest(f.Kundan.Id, 30m, null)]), Actor);

        result!.Lines.Should().ContainSingle();
        OnHand(db, f.Earbuds.Id).Should().Be(50m);
        OnHand(db, f.Kundan.Id).Should().Be(70m, "the untouched line must not move again");
    }

    [Fact]
    public async Task UpdateAsync_UnchangedLines_MoveNoStockAtAll()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCreate(f, [new ShipmentLineRequest(f.Kundan.Id, 30m, null)]), Actor);

        await sut.UpdateAsync(created.Id, Update(f, [new ShipmentLineRequest(f.Kundan.Id, 30m, null)]) with { Destination = "Pune" }, Actor);

        OnHand(db, f.Kundan.Id).Should().Be(70m, "a zero delta must be a no-op, not a re-decrement");
    }

    [Fact]
    public async Task UpdateAsync_RaisingBeyondAvailable_Throws_AndCommitsNothing()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCreate(f, [new ShipmentLineRequest(f.Kundan.Id, 30m, null)]), Actor);

        var act = () => sut.UpdateAsync(created.Id, Update(f, [new ShipmentLineRequest(f.Kundan.Id, 500m, null)]), Actor);

        var ex = (await act.Should().ThrowAsync<InsufficientStockException>()).And;
        ex.Items.Should().ContainSingle().Which.AvailableQty.Should().Be(70m, "availability is measured against the post-create balance");
        OnHand(db, f.Kundan.Id).Should().Be(70m);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotChangeStatus_EvenThoughEverythingElseChanges()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);

        var result = await sut.UpdateAsync(created.Id, Update(f, null) with { CustomerId = f.OtherCustomer.Id }, Actor);

        result!.Status.Code.Should().Be("PACKED");
        result.StatusHistory.Should().ContainSingle("no transition happened, so no history row is appended");
        // UpdateShipmentRequest carries no StatusId, so status genuinely cannot be set here.
        typeof(UpdateShipmentRequest).GetProperty("StatusId").Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ChangingToFreightOnlyWithLinesStillPresent_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCreate(f, [new ShipmentLineRequest(f.Kundan.Id, 5m, null)]), Actor);

        var act = () => sut.UpdateAsync(created.Id,
            Update(f, [new ShipmentLineRequest(f.Kundan.Id, 5m, null)]) with { ServiceTypeId = f.FreightOnly.Id }, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("lines");
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        (await sut.UpdateAsync(Guid.NewGuid(), Update(f, null), Actor)).Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_CustomerWithNoBusinessName_FallsBackToContactName()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);

        var result = await sut.UpdateAsync(created.Id, Update(f, null) with { CustomerId = f.OtherCustomer.Id }, Actor);

        result!.Customer.Name.Should().Be("Suresh Kumar");
    }

    private static UpdateShipmentRequest Update(Fixture f, IReadOnlyList<ShipmentLineRequest>? lines) =>
        new(f.Customer.Id, "Surat, Gujarat", f.Cif.Id, new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc),
            FreightCost: 48_000m, TotalValue: null, Mode: "Sea LCL", AwbOrBl: "BL X", Eta: null, Lines: lines);

    // ---- D-j: delete restores stock -------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_RestoresTheStockItsLinesConsumed()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit);
        var created = await sut.CreateAsync(ValidCreate(f, [
            new ShipmentLineRequest(f.Kundan.Id, 30m, null),
            new ShipmentLineRequest(f.Earbuds.Id, 5m, null)
        ]), Actor);

        (await sut.DeleteAsync(created.Id, Actor)).Should().BeTrue();

        OnHand(db, f.Kundan.Id).Should().Be(100m);
        OnHand(db, f.Earbuds.Id).Should().Be(50m);
        audit.Verify(a => a.LogAsync(Actor, "ShipmentDeleted", "Shipment", created.Id.ToString(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_RestoreIsNotBlockedByTheNegativeGuard()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(
            ValidCreate(f, [new ShipmentLineRequest(f.Kundan.Id, 140m, null)]) with { AllowNegativeStock = true }, Actor);
        OnHand(db, f.Kundan.Id).Should().Be(-40m);

        (await sut.DeleteAsync(created.Id, Actor)).Should().BeTrue();

        OnHand(db, f.Kundan.Id).Should().Be(100m, "a restore only ever raises stock, so the guard must not apply");
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        (await sut.DeleteAsync(Guid.NewGuid(), Actor)).Should().BeFalse();
    }

    // ---- E7-08: list, filters and status counts --------------------------------------------------------------

    [Fact]
    public async Task ListAsync_StatusCountsSpanTheWholeSet_NotTheSelectedTab()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var a = await sut.CreateAsync(ValidCreate(f), Actor);
        var b = await sut.CreateAsync(ValidCreate(f), Actor);
        await sut.CreateAsync(ValidCreate(f), Actor);
        await sut.ChangeStatusAsync(a.Id, new ChangeShipmentStatusRequest(f.InTransit.Id, null), Actor);
        await sut.ChangeStatusAsync(b.Id, new ChangeShipmentStatusRequest(f.InTransit.Id, null), Actor);

        var result = await sut.ListAsync(new ShipmentListQuery(null, 1, 25, StatusId: f.Packed.Id, null, null, null));

        result.Items.Should().ContainSingle("the PACKED tab holds one shipment");
        result.TotalCount.Should().Be(1);
        result.StatusCounts.Single(c => c.Code == "PACKED").Count.Should().Be(1);
        result.StatusCounts.Single(c => c.Code == "IN TRANSIT").Count.Should().Be(2, "the other tabs must still show their own totals");
        result.StatusCounts.Single(c => c.Code == "DISPATCHED").Count.Should().Be(0, "a zero-count status still gets a tab");
    }

    [Fact]
    public async Task ListAsync_StatusCountsAreOrderedBySortOrder()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var result = await sut.ListAsync(new ShipmentListQuery(null, 1, 25, null, null, null, null));

        result.StatusCounts.Select(c => c.Code).Should().ContainInOrder("PACKED", "DISPATCHED", "IN TRANSIT");
    }

    [Fact]
    public async Task ListAsync_StatusCountsStillHonourTheOtherFilters()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        await sut.CreateAsync(ValidCreate(f), Actor);
        await sut.CreateAsync(ValidCreate(f) with { CustomerId = f.OtherCustomer.Id }, Actor);

        var result = await sut.ListAsync(new ShipmentListQuery(null, 1, 25, null, CustomerId: f.Customer.Id, null, null));

        result.StatusCounts.Single(c => c.Code == "PACKED").Count.Should().Be(1, "only the status filter is excluded from the counts, not the rest");
    }

    [Fact]
    public async Task ListAsync_DateRangeFiltersOnDispatchDate()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        await sut.CreateAsync(ValidCreate(f) with { DispatchDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc) }, Actor);
        await sut.CreateAsync(ValidCreate(f) with { DispatchDate = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc) }, Actor);

        var result = await sut.ListAsync(new ShipmentListQuery(null, 1, 25, null, null,
            From: new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc), To: new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc)));

        result.Items.Should().ContainSingle().Which.DispatchDate.Should().Be(new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ListAsync_InvertedDateRange_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var act = () => sut.ListAsync(new ShipmentListQuery(null, 1, 25, null, null,
            From: new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), To: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)));

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("from");
    }

    [Fact]
    public async Task ListAsync_SearchMatchesOnReference()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var first = await sut.CreateAsync(ValidCreate(f), Actor);
        await sut.CreateAsync(ValidCreate(f), Actor);

        var result = await sut.ListAsync(new ShipmentListQuery(first.Reference, 1, 25, null, null, null, null));

        result.Items.Should().ContainSingle().Which.Reference.Should().Be(first.Reference);
    }

    [Fact]
    public async Task ListAsync_OrdersByDispatchDateDescending()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        await sut.CreateAsync(ValidCreate(f) with { DispatchDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc) }, Actor);
        await sut.CreateAsync(ValidCreate(f) with { DispatchDate = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc) }, Actor);
        await sut.CreateAsync(ValidCreate(f) with { DispatchDate = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc) }, Actor);

        var result = await sut.ListAsync(new ShipmentListQuery(null, 1, 25, null, null, null, null));

        result.Items.Select(i => i.DispatchDate).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task ListAsync_ListRowCarriesLineCount()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        await sut.CreateAsync(ValidCreate(f, [
            new ShipmentLineRequest(f.Kundan.Id, 1m, null),
            new ShipmentLineRequest(f.Earbuds.Id, 1m, null)
        ]), Actor);

        var result = await sut.ListAsync(new ShipmentListQuery(null, 1, 25, null, null, null, null));

        result.Items.Should().ContainSingle().Which.LineCount.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        (await sut.GetAsync(Guid.NewGuid())).Should().BeNull();
    }
}
