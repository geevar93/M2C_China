using FluentAssertions;
using Moq;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Application.Invoicing;
using SourcingOps.Application.Tests.TestSupport;
using SourcingOps.Domain.Constants;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Application.Tests.Invoicing;

/// <summary>
/// Covers ACTION_PLAN E8-01…E8-08 per the M6 contract. Weighted towards the failure/validation
/// paths per the §7 DoD: the Draft-only edit lock, the status-transition graph (branching on
/// Code, never Label — D-50 precedent), the mark-paid precondition, and the "fail loudly when
/// CompanySettings is unconfigured" rule that gates DRAFT→ISSUED's PDF render. Every identity
/// assertion below terminates in a literal, never in the constant it is checking, per D-64.
///
/// Number generation's COLLISION-retry path is deliberately not asserted here — same reasoning
/// as ShipmentServiceTests: the EF Core InMemory provider enforces no unique index. It is
/// covered by the integration suite (see N-18's forced-collision test in
/// ShipmentsEndpointTests, whose technique this pass also proves for invoices via the
/// Api.Tests InvoicesEndpointTests suite).
/// </summary>
public class InvoiceServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private static InvoiceService CreateSut(
        AppDbContext db, out Mock<IAuditLogger> auditMock, out Mock<IFileStorage> storageMock, out Mock<IInvoicePdfRenderer> rendererMock)
    {
        auditMock = new Mock<IAuditLogger>();
        storageMock = new Mock<IFileStorage>();
        storageMock
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, CancellationToken _) => path);
        rendererMock = new Mock<IInvoicePdfRenderer>();
        rendererMock.Setup(r => r.Render(It.IsAny<InvoicePdfModel>())).Returns([0x25, 0x50, 0x44, 0x46]);
        return new InvoiceService(db, auditMock.Object, storageMock.Object, rendererMock.Object);
    }

    private sealed record Fixture(Customer Customer, ServiceType Cif, Shipment Shipment, InvoiceStatus Draft, InvoiceStatus Issued, InvoiceStatus Paid, InvoiceStatus Cancelled);

    private static Fixture SeedMasterData(AppDbContext db)
    {
        var cif = new ServiceType { Id = Guid.NewGuid(), Code = SeedDefaults.ServiceTypeCif, Label = "CIF", IsActive = true, SortOrder = 1 };
        db.ServiceTypes.Add(cif);

        var leadStatus = new LeadStatus { Id = Guid.NewGuid(), Code = "ACTIVE", Label = "Active", IsActive = true, SortOrder = 1 };
        db.LeadStatuses.Add(leadStatus);

        var customer = new Customer { Id = Guid.NewGuid(), Name = "Meena Patel", BusinessName = "Meena Traders", Phone = "+919000000001", StateCode = "24", ServiceTypeId = cif.Id, ServiceType = cif, StatusId = leadStatus.Id, CreatedAt = DateTime.UtcNow };
        db.Customers.Add(customer);

        var shipmentStatus = new ShipmentStatus { Id = Guid.NewGuid(), Code = "PACKED", Label = "Packed", IsActive = true, SortOrder = 1 };
        db.ShipmentStatuses.Add(shipmentStatus);
        var shipment = new Shipment { Id = Guid.NewGuid(), Reference = "SHP-2608-001", CustomerId = customer.Id, Customer = customer, ServiceTypeId = cif.Id, ServiceType = cif, StatusId = shipmentStatus.Id, Status = shipmentStatus, CreatedAt = DateTime.UtcNow };
        db.Shipments.Add(shipment);

        var draft = new InvoiceStatus { Id = Guid.NewGuid(), Code = InvoiceStatusCodes.Draft, Label = "Draft", IsActive = true, SortOrder = 1 };
        var issued = new InvoiceStatus { Id = Guid.NewGuid(), Code = InvoiceStatusCodes.Issued, Label = "Issued", IsActive = true, SortOrder = 2 };
        var paid = new InvoiceStatus { Id = Guid.NewGuid(), Code = InvoiceStatusCodes.Paid, Label = "Paid", IsActive = true, SortOrder = 3 };
        var cancelled = new InvoiceStatus { Id = Guid.NewGuid(), Code = InvoiceStatusCodes.Cancelled, Label = "Cancelled", IsActive = true, SortOrder = 4 };
        db.InvoiceStatuses.AddRange(draft, issued, paid, cancelled);

        db.Users.Add(new User { Id = Actor, Name = "Vikram Nair", Email = $"actor-{Guid.NewGuid():N}@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });

        db.SaveChanges();
        return new Fixture(customer, cif, shipment, draft, issued, paid, cancelled);
    }

    /// <summary>
    /// One line at qty 1 x 1,000 at 18% — reproducing the 1,000 + 180 the hand-entered
    /// Amount/TaxAmount used to carry, so the existing total assertions still pin the same
    /// figures now that both are derived rather than supplied.
    /// </summary>
    private static CreateInvoiceRequest ValidCreate(Fixture f, Guid? shipmentId = null) =>
        new(f.Customer.Id, shipmentId, new DateOnly(2026, 8, 1), "Consulting services", "INR", [Line(1000m, 18m)]);

    private static UpsertInvoiceLineRequest Line(decimal unitPrice, decimal gstRate, decimal quantity = 1m) =>
        new(null, "Consulting services", "998311", quantity, unitPrice, gstRate);

    /// <summary>
    /// State code 24 matches the seeded customer's, so the fixture's default supply is
    /// INTRA-state (CGST + SGST). Tests that need the inter-state path move the customer.
    /// </summary>
    private static CompanySettings ValidCompanySettings() => new()
    {
        Id = CompanySettings.SingletonId,
        LegalEntityName = "M2C Sourcing Pvt Ltd",
        RegisteredAddress = "123 Industrial Estate, Surat, Gujarat",
        StateCode = "24"
    };

    // ---- Create (E8-01) -----------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_Valid_CreatesAsDraft_WithGeneratedNumber_AndHistoryRow()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var result = await sut.CreateAsync(ValidCreate(f), Actor);

        result.Status.Code.Should().Be("DRAFT");
        result.InvoiceNumber.Should().MatchRegex(@"^INV-\d{4}-\d{3}$");
        result.TotalAmount.Should().Be(1180m);
        result.StatusHistory.Should().ContainSingle();
        result.HasPdf.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_UnknownCustomer_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var act = () => sut.CreateAsync(ValidCreate(f) with { CustomerId = Guid.NewGuid() }, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("customerId");
    }

    [Fact]
    public async Task CreateAsync_ShipmentBelongingToAnotherCustomer_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var otherCustomer = new Customer { Id = Guid.NewGuid(), Name = "Other Co", Phone = "+919000000002", ServiceTypeId = f.Cif.Id, StatusId = db.LeadStatuses.First().Id, CreatedAt = DateTime.UtcNow };
        db.Customers.Add(otherCustomer);
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _, out _, out _);

        var act = () => sut.CreateAsync(ValidCreate(f, f.Shipment.Id) with { CustomerId = otherCustomer.Id }, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("shipmentId");
    }

    [Fact]
    public async Task CreateAsync_NegativeLineUnitPrice_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var act = () => sut.CreateAsync(ValidCreate(f) with { Lines = [Line(-1m, 18m)] }, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("lines[0].unitPrice");
    }

    [Fact]
    public async Task CreateAsync_NoLines_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var act = () => sut.CreateAsync(ValidCreate(f) with { Lines = [] }, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("lines");
    }

    [Fact]
    public async Task CreateAsync_DerivesAmountAndTaxFromTheLines_RatherThanAcceptingThem()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        // 2 x 2,500 at 12% = 5,000 taxable + 600 tax.
        var result = await sut.CreateAsync(ValidCreate(f) with { Lines = [Line(2500m, 12m, quantity: 2m)] }, Actor);

        result.Amount.Should().Be(5000m);
        result.TaxAmount.Should().Be(600m);
        result.TotalAmount.Should().Be(5600m);
    }

    [Fact]
    public async Task CreateAsync_TwoInvoicesSameMonth_GetSequentialNumbers()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var first = await sut.CreateAsync(ValidCreate(f), Actor);
        var second = await sut.CreateAsync(ValidCreate(f), Actor);

        first.InvoiceNumber.Should().NotBe(second.InvoiceNumber);
        int.Parse(second.InvoiceNumber.Split('-')[2]).Should().Be(int.Parse(first.InvoiceNumber.Split('-')[2]) + 1);
    }

    [Fact]
    public async Task CreateAsync_CompanySettingsPrefixConfigured_UsesItInsteadOfDefault()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        db.CompanySettings.Add(new CompanySettings { Id = CompanySettings.SingletonId, InvoiceNumberPrefix = " gst " });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _, out _, out _);

        var result = await sut.CreateAsync(ValidCreate(f), Actor);

        result.InvoiceNumber.Should().StartWith("GST-", "the prefix is trimmed and uppercased (M6 contract §1)");
    }

    // ---- List / per-status counts + sums (N-31/D-72) ---------------------------------------

    private static InvoiceListQuery AllQuery(Guid? statusId = null) =>
        new(null, 1, 25, null, statusId, null, null, null);

    [Fact]
    public async Task ListAsync_StatusCounts_SumAmountPlusTaxAmountPerStatus()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        // Two DRAFT invoices: 1000+180 and 500+50 -> DRAFT total 1730.
        await sut.CreateAsync(ValidCreate(f), Actor);
        await sut.CreateAsync(ValidCreate(f) with { Lines = [Line(500m, 10m)] }, Actor); // 500 + 50

        var result = await sut.ListAsync(AllQuery());

        var draftCount = result.StatusCounts.Single(c => c.Code == "DRAFT");
        draftCount.Count.Should().Be(2);
        draftCount.TotalAmount.Should().Be(1730m);
    }

    [Fact]
    public async Task ListAsync_StatusCounts_StatusesWithNoInvoices_ZeroFillBothCountAndTotalAmount()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        await sut.CreateAsync(ValidCreate(f), Actor);

        var result = await sut.ListAsync(AllQuery());

        var paidCount = result.StatusCounts.Single(c => c.Code == "PAID");
        paidCount.Count.Should().Be(0);
        paidCount.TotalAmount.Should().Be(0m);
    }

    [Fact]
    public async Task ListAsync_StatusCounts_OrderedBySortOrderThenCode()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var result = await sut.ListAsync(AllQuery());

        result.StatusCounts.Select(c => c.Code).Should().Equal("DRAFT", "ISSUED", "PAID", "CANCELLED");
    }

    /// <summary>
    /// E7-08 semantics reused verbatim (M6 contract §3): the status counts/sums are computed over
    /// EVERY filter except the status filter, so selecting a status tab must not change the
    /// other tabs' figures. This is the test that matters most for N-31.
    /// </summary>
    [Fact]
    public async Task ListAsync_StatusCounts_StatusFilterDoesNotChangeOtherStatuses_CountsOrSums()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out var storageMock, out _);

        await sut.CreateAsync(ValidCreate(f), Actor); // DRAFT 1000+180
        var toIssue = await sut.CreateAsync(ValidCreate(f) with { Lines = [Line(2000m, 10m)] }, Actor); // 2000 + 200
        db.CompanySettings.Add(ValidCompanySettings());
        await db.SaveChangesAsync();
        await sut.ChangeStatusAsync(toIssue.Id, new ChangeInvoiceStatusRequest(f.Issued.Id, null), Actor); // ISSUED 2200

        var unfiltered = await sut.ListAsync(AllQuery());
        var filteredToIssued = await sut.ListAsync(AllQuery(f.Issued.Id));

        var draftUnfiltered = unfiltered.StatusCounts.Single(c => c.Code == "DRAFT");
        var draftFiltered = filteredToIssued.StatusCounts.Single(c => c.Code == "DRAFT");
        draftFiltered.Count.Should().Be(draftUnfiltered.Count);
        draftFiltered.TotalAmount.Should().Be(draftUnfiltered.TotalAmount);
        draftFiltered.TotalAmount.Should().Be(1180m);

        var issuedFiltered = filteredToIssued.StatusCounts.Single(c => c.Code == "ISSUED");
        issuedFiltered.TotalAmount.Should().Be(2200m);
    }

    // ---- Update (E8-01, editable only in Draft) ------------------------------------------

    [Fact]
    public async Task UpdateAsync_WhileDraft_Succeeds()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);

        var update = new UpdateInvoiceRequest(f.Customer.Id, null, created.InvoiceDate, "Updated description", "INR", [Line(2000m, 18m)]);
        var result = await sut.UpdateAsync(created.Id, update, Actor);

        result!.Amount.Should().Be(2000m);
        result.LineDescription.Should().Be("Updated description");
    }

    [Fact]
    public async Task UpdateAsync_AfterIssued_ThrowsConflict()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        db.CompanySettings.Add(ValidCompanySettings());
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _, out _, out _);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);
        await sut.ChangeStatusAsync(created.Id, new ChangeInvoiceStatusRequest(f.Issued.Id, null), Actor);

        var update = new UpdateInvoiceRequest(f.Customer.Id, null, created.InvoiceDate, "x", "INR", [Line(1m, 0m)]);
        var act = () => sut.UpdateAsync(created.Id, update, Actor);

        var ex = await act.Should().ThrowAsync<InvoiceConflictException>();
        ex.Which.Extensions["currentStatus"].Should().Be("ISSUED");
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var result = await sut.UpdateAsync(Guid.NewGuid(), new UpdateInvoiceRequest(Guid.NewGuid(), null, new DateOnly(2026, 8, 1), null, "INR", [Line(1m, 0m)]), Actor);

        result.Should().BeNull();
    }

    // ---- Status transitions (E8-02, E8-03) -----------------------------------------------

    // ---- Place of supply and the issue-time completeness gate ----------------------------

    /// <summary>
    /// Seeds settings and issues, returning the issued detail. Kept local to these tests so the
    /// place-of-supply cases read as one flow rather than three setup lines each.
    /// </summary>
    private static async Task<InvoiceDetailDto?> IssueAsync(InvoiceService sut, AppDbContext db, Fixture f, CreateInvoiceRequest create, CompanySettings? settings = null)
    {
        db.CompanySettings.Add(settings ?? ValidCompanySettings());
        await db.SaveChangesAsync();
        var created = await sut.CreateAsync(create, Actor);
        return await sut.ChangeStatusAsync(created.Id, new ChangeInvoiceStatusRequest(f.Issued.Id, null), Actor);
    }

    [Fact]
    public async Task ChangeStatusAsync_DraftToIssued_SameState_SplitsTaxIntoCgstAndSgst()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db); // customer state 24, seller state 24
        var sut = CreateSut(db, out _, out _, out _);

        var issued = await IssueAsync(sut, db, f, ValidCreate(f));

        issued!.TaxSummary.IsIntraState.Should().BeTrue();
        issued.TaxSummary.PlaceOfSupplyStateCode.Should().Be("24");
        issued.TaxSummary.PlaceOfSupplyStateName.Should().Be("Gujarat");
        issued.TaxSummary.CgstAmount.Should().Be(90m);
        issued.TaxSummary.SgstAmount.Should().Be(90m);
        issued.TaxSummary.IgstAmount.Should().Be(0m);
        issued.TaxAmount.Should().Be(180m);
    }

    [Fact]
    public async Task ChangeStatusAsync_DraftToIssued_DifferentState_ChargesIgst()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        f.Customer.StateCode = "27"; // Maharashtra buyer, Gujarat seller
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _, out _, out _);

        var issued = await IssueAsync(sut, db, f, ValidCreate(f));

        issued!.TaxSummary.IsIntraState.Should().BeFalse();
        issued.TaxSummary.PlaceOfSupplyStateCode.Should().Be("27");
        issued.TaxSummary.IgstAmount.Should().Be(180m);
        issued.TaxSummary.CgstAmount.Should().Be(0m);
        issued.TaxSummary.SgstAmount.Should().Be(0m);
    }

    [Fact]
    public async Task ChangeStatusAsync_DraftToIssued_CustomerWithNoStateCodeOrGstin_DefaultsToSellerState()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        f.Customer.StateCode = null;
        f.Customer.Gstin = null;
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _, out _, out _);

        var issued = await IssueAsync(sut, db, f, ValidCreate(f));

        issued!.TaxSummary.IsIntraState.Should().BeTrue("a customer with no GST state is treated as being in the seller's state");
        issued.TaxSummary.PlaceOfSupplyStateCode.Should().Be("24");
        issued.TaxSummary.IgstAmount.Should().Be(0m);
    }

    [Fact]
    public async Task ChangeStatusAsync_DraftToIssued_CustomerStateDerivedFromGstin_WhenStateCodeIsBlank()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        f.Customer.StateCode = null;
        f.Customer.Gstin = "27ABCDE1234F1Z5"; // 27 = Maharashtra
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _, out _, out _);

        var issued = await IssueAsync(sut, db, f, ValidCreate(f));

        issued!.TaxSummary.PlaceOfSupplyStateCode.Should().Be("27");
        issued.TaxSummary.IsIntraState.Should().BeFalse();
    }

    [Fact]
    public async Task ChangeStatusAsync_DraftToIssued_SellerWithNoStateCodeOrGstin_ThrowsValidation()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var settings = ValidCompanySettings();
        settings.StateCode = null;
        settings.Gstin = null;

        var act = () => IssueAsync(sut, db, f, ValidCreate(f), settings);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("companySettings");
    }

    [Fact]
    public async Task ChangeStatusAsync_DraftToIssued_LineWithNoGstRate_ThrowsValidation()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var noRate = ValidCreate(f) with { Lines = [new UpsertInvoiceLineRequest(null, "Consulting", "998311", 1m, 1000m, null)] };
        var act = () => IssueAsync(sut, db, f, noRate);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("lines");
    }

    [Fact]
    public async Task ChangeStatusAsync_DraftToIssued_LineWithNoHsnCode_ThrowsValidation()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var noHsn = ValidCreate(f) with { Lines = [new UpsertInvoiceLineRequest(null, "Consulting", null, 1m, 1000m, 18m)] };
        var act = () => IssueAsync(sut, db, f, noHsn);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("lines");
    }

    [Fact]
    public async Task ChangeStatusAsync_DraftToIssued_LineWithNoUnitPrice_ThrowsValidation()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        // No price on the request AND no SellingPrice on any item - the line lands at 0, which
        // is an unset price, not a giveaway.
        var noPrice = ValidCreate(f) with { Lines = [new UpsertInvoiceLineRequest(null, "Consulting", "998311", 1m, null, 18m)] };
        var act = () => IssueAsync(sut, db, f, noPrice);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("lines");
    }

    // ---- Unit price / HSN / rate default from the inventory item -------------------------

    [Fact]
    public async Task CreateAsync_LineWithAnItem_DefaultsPriceHsnAndRateFromTheItem_SoNoneIsManualEntry()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = SeedItem(db, sellingPrice: 120m, unitCost: 40m);
        var sut = CreateSut(db, out _, out _, out _);

        var request = ValidCreate(f) with { Lines = [new UpsertInvoiceLineRequest(item.Id, null, null, 10m, null, null)] };
        var result = await sut.CreateAsync(request, Actor);

        var line = result.Lines.Single();
        line.Description.Should().Be("Brass Hinge");
        line.UnitPrice.Should().Be(120m);   // SellingPrice, NOT the 40 UnitCost
        line.HsnCode.Should().Be("8302");
        line.GstRate.Should().Be(18m);
        line.TaxableValue.Should().Be(1200m);
    }

    [Fact]
    public async Task CreateAsync_LineWithAnItemThatHasNoSellingPrice_DoesNotFallBackToUnitCost()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = SeedItem(db, sellingPrice: null, unitCost: 40m);
        var sut = CreateSut(db, out _, out _, out _);

        var request = ValidCreate(f) with { Lines = [new UpsertInvoiceLineRequest(item.Id, null, null, 10m, null, null)] };
        var result = await sut.CreateAsync(request, Actor);

        // 0, not 40: billing a customer at cost would silently discard the whole margin.
        result.Lines.Single().UnitPrice.Should().Be(0m);
    }

    [Fact]
    public async Task CreateAsync_ExplicitLineValues_OverrideTheItemDefaults()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var item = SeedItem(db, sellingPrice: 120m, unitCost: null);
        var sut = CreateSut(db, out _, out _, out _);

        var request = ValidCreate(f) with { Lines = [new UpsertInvoiceLineRequest(item.Id, "Special order hinge", "8302", 2m, 150m, 12m)] };
        var result = await sut.CreateAsync(request, Actor);

        var line = result.Lines.Single();
        line.Description.Should().Be("Special order hinge");
        line.UnitPrice.Should().Be(150m);
        line.GstRate.Should().Be(12m);
    }

    [Fact]
    public async Task CreateAsync_PercentDiscount_ReducesTheTaxableValueBeforeGst()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var request = ValidCreate(f) with { Lines = [Line(500m, 18m, quantity: 2m) with { DiscountType = "percent", DiscountValue = 10m }] };
        var result = await sut.CreateAsync(request, Actor);

        var line = result.Lines.Single();
        line.DiscountType.Should().Be("PERCENT");
        line.DiscountValue.Should().Be(10m);
        line.GrossValue.Should().Be(1000m);
        line.DiscountAmount.Should().Be(100m);
        line.TaxableValue.Should().Be(900m);
        result.Amount.Should().Be(900m);
        result.TaxAmount.Should().Be(162m);
        result.TotalAmount.Should().Be(1062m);
        result.TaxSummary.GrossValue.Should().Be(1000m);
        result.TaxSummary.TotalDiscount.Should().Be(100m);
    }

    [Fact]
    public async Task CreateAsync_AmountDiscount_IsTakenOffTheWholeLine()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var request = ValidCreate(f) with { Lines = [Line(100m, 18m, quantity: 10m) with { DiscountType = "AMOUNT", DiscountValue = 250m }] };
        var result = await sut.CreateAsync(request, Actor);

        result.Lines.Single().DiscountAmount.Should().Be(250m);
        result.Amount.Should().Be(750m);
        result.TaxAmount.Should().Be(135m);
    }

    [Fact]
    public async Task CreateAsync_ZeroDiscount_IsStoredAsNoDiscount()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var request = ValidCreate(f) with { Lines = [Line(100m, 18m) with { DiscountType = "PERCENT", DiscountValue = 0m }] };
        var result = await sut.CreateAsync(request, Actor);

        result.Lines.Single().DiscountType.Should().BeNull();
        result.Lines.Single().DiscountValue.Should().BeNull();
        result.Amount.Should().Be(100m);
    }

    [Theory]
    [InlineData("AMOUNT", 100.01)]
    [InlineData("PERCENT", 100.5)]
    [InlineData("PERCENT", -5)]
    public async Task CreateAsync_OutOfRangeDiscount_ThrowsForThatLine(string type, double value)
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var request = ValidCreate(f) with { Lines = [Line(100m, 18m) with { DiscountType = type, DiscountValue = (decimal)value }] };
        var act = () => sut.CreateAsync(request, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("lines[0].discountValue");
    }

    [Fact]
    public async Task CreateAsync_UnknownDiscountType_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var request = ValidCreate(f) with { Lines = [Line(100m, 18m) with { DiscountType = "BOGO", DiscountValue = 5m }] };
        var act = () => sut.CreateAsync(request, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("lines[0].discountType");
    }

    [Fact]
    public async Task CreateAsync_UnknownInventoryItem_ThrowsWithTheOffendingLineIndex()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var request = ValidCreate(f) with
        {
            Lines = [Line(100m, 18m), new UpsertInvoiceLineRequest(Guid.NewGuid(), "Ghost", "1234", 1m, 10m, 5m)]
        };
        var act = () => sut.CreateAsync(request, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("lines[1].inventoryItemId");
    }

    [Fact]
    public async Task UpdateAsync_ReplacesLinesWholesale_AndRederivesTheTotals()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);
        created.Lines.Should().HaveCount(1);

        var update = new UpdateInvoiceRequest(f.Customer.Id, null, created.InvoiceDate, null, "INR",
            [Line(100m, 5m), Line(200m, 12m)]);
        var result = await sut.UpdateAsync(created.Id, update, Actor);

        result!.Lines.Should().HaveCount(2);
        result.Amount.Should().Be(300m);      // 100 + 200 taxable
        result.TaxAmount.Should().Be(29m);    // 5 + 24
    }

    private static InventoryItem SeedItem(AppDbContext db, decimal? sellingPrice, decimal? unitCost)
    {
        var category = new Category { Id = Guid.NewGuid(), Name = "Hardware", IsActive = true, SortOrder = 1 };
        db.Categories.Add(category);
        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            Name = "Brass Hinge",
            CategoryId = category.Id,
            UnitCost = unitCost,
            SellingPrice = sellingPrice,
            HsnCode = "8302",
            GstRate = 18m
        };
        db.InventoryItems.Add(item);
        db.SaveChanges();
        return item;
    }

    [Fact]
    public async Task ChangeStatusAsync_DraftToIssued_WithoutCompanySettings_ThrowsValidation()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);

        var act = () => sut.ChangeStatusAsync(created.Id, new ChangeInvoiceStatusRequest(f.Issued.Id, null), Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("companySettings");
    }

    [Fact]
    public async Task ChangeStatusAsync_DraftToIssued_WithCompanySettingsMissingRequiredField_ThrowsValidation()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        // LegalEntityName set, RegisteredAddress still null — both are required (M6 contract §0).
        db.CompanySettings.Add(new CompanySettings { Id = CompanySettings.SingletonId, LegalEntityName = "M2C Sourcing Pvt Ltd" });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _, out _, out _);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);

        var act = () => sut.ChangeStatusAsync(created.Id, new ChangeInvoiceStatusRequest(f.Issued.Id, null), Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("companySettings");
    }

    [Fact]
    public async Task ChangeStatusAsync_DraftToIssued_WithCompanySettings_RendersAndStoresPdf()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        db.CompanySettings.Add(ValidCompanySettings());
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _, out var storageMock, out var rendererMock);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);

        var result = await sut.ChangeStatusAsync(created.Id, new ChangeInvoiceStatusRequest(f.Issued.Id, "Sent to customer"), Actor);

        result!.Status.Code.Should().Be("ISSUED");
        result.HasPdf.Should().BeTrue();
        result.StatusHistory.Should().HaveCount(2);
        rendererMock.Verify(r => r.Render(It.IsAny<InvoicePdfModel>()), Times.Once);
        storageMock.Verify(s => s.SaveAsync($"invoices/{created.Id}.pdf", It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>N-37: the buyer's GSTIN (distinct from the seller's on CompanySettings) flows into the PDF model.</summary>
    [Fact]
    public async Task ChangeStatusAsync_CustomerHasGstin_PopulatesCustomerGstinOnPdfModel()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        f.Customer.Gstin = "27ABCDE1234F1Z5";
        db.CompanySettings.Add(ValidCompanySettings());
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _, out _, out var rendererMock);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);

        await sut.ChangeStatusAsync(created.Id, new ChangeInvoiceStatusRequest(f.Issued.Id, null), Actor);

        rendererMock.Verify(r => r.Render(It.Is<InvoicePdfModel>(m => m.CustomerGstin == "27ABCDE1234F1Z5")), Times.Once);
    }

    /// <summary>N-37: a customer with no GSTIN on file must not leak a stray non-null value onto the PDF model.</summary>
    [Fact]
    public async Task ChangeStatusAsync_CustomerHasNoGstin_PopulatesNullCustomerGstinOnPdfModel()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        // f.Customer.Gstin left null (the default from SeedMasterData).
        db.CompanySettings.Add(ValidCompanySettings());
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _, out _, out var rendererMock);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);

        await sut.ChangeStatusAsync(created.Id, new ChangeInvoiceStatusRequest(f.Issued.Id, null), Actor);

        rendererMock.Verify(r => r.Render(It.Is<InvoicePdfModel>(m => m.CustomerGstin == null)), Times.Once);
    }

    [Theory]
    [InlineData("DRAFT", "PAID")]
    [InlineData("ISSUED", "DRAFT")]
    [InlineData("PAID", "ISSUED")]
    [InlineData("CANCELLED", "DRAFT")]
    public async Task ChangeStatusAsync_IllegalTransition_ThrowsConflict_NamingBothCodes(string fromCode, string toCode)
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        db.CompanySettings.Add(ValidCompanySettings());
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _, out _, out _);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);

        var statusesByCode = new Dictionary<string, InvoiceStatus> { ["DRAFT"] = f.Draft, ["ISSUED"] = f.Issued, ["PAID"] = f.Paid, ["CANCELLED"] = f.Cancelled };

        // Drive the invoice to `fromCode` first via whatever legal path gets there.
        if (fromCode != "DRAFT")
        {
            await sut.ChangeStatusAsync(created.Id, new ChangeInvoiceStatusRequest(f.Issued.Id, null), Actor);
        }
        if (fromCode == "PAID")
        {
            await sut.MarkPaidAsync(created.Id, new MarkInvoicePaidRequest(null, null), Actor);
        }
        if (fromCode == "CANCELLED")
        {
            await sut.ChangeStatusAsync(created.Id, new ChangeInvoiceStatusRequest(f.Cancelled.Id, null), Actor);
        }

        var act = () => sut.ChangeStatusAsync(created.Id, new ChangeInvoiceStatusRequest(statusesByCode[toCode].Id, null), Actor);

        var ex = await act.Should().ThrowAsync<InvoiceConflictException>();
        ex.Which.Extensions["fromStatus"].Should().Be(fromCode);
        ex.Which.Extensions["toStatus"].Should().Be(toCode);
    }

    [Fact]
    public async Task ChangeStatusAsync_UnknownStatus_ThrowsValidation()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);

        var act = () => sut.ChangeStatusAsync(created.Id, new ChangeInvoiceStatusRequest(Guid.NewGuid(), null), Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("statusId");
    }

    [Fact]
    public async Task ChangeStatusAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var result = await sut.ChangeStatusAsync(Guid.NewGuid(), new ChangeInvoiceStatusRequest(f.Issued.Id, null), Actor);

        result.Should().BeNull();
    }

    // ---- Mark paid (E8-07) --------------------------------------------------------------

    [Fact]
    public async Task MarkPaidAsync_WhileIssued_Succeeds_AndRecordsHistory()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        db.CompanySettings.Add(ValidCompanySettings());
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _, out _, out _);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);
        await sut.ChangeStatusAsync(created.Id, new ChangeInvoiceStatusRequest(f.Issued.Id, null), Actor);

        var result = await sut.MarkPaidAsync(created.Id, new MarkInvoicePaidRequest(null, "NEFT-12345"), Actor);

        result!.Status.Code.Should().Be("PAID");
        result.PaidAt.Should().NotBeNull();
        result.PaidReference.Should().Be("NEFT-12345");
        result.StatusHistory.Should().HaveCount(3);
    }

    [Fact]
    public async Task MarkPaidAsync_WhileDraft_ThrowsConflict()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);

        var act = () => sut.MarkPaidAsync(created.Id, new MarkInvoicePaidRequest(null, null), Actor);

        var ex = await act.Should().ThrowAsync<InvoiceConflictException>();
        ex.Which.Extensions["fromStatus"].Should().Be("DRAFT");
        ex.Which.Extensions["toStatus"].Should().Be("PAID");
    }

    [Fact]
    public async Task MarkPaidAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var result = await sut.MarkPaidAsync(Guid.NewGuid(), new MarkInvoicePaidRequest(null, null), Actor);

        result.Should().BeNull();
    }

    // ---- PDF download (E8-03) ------------------------------------------------------------

    [Fact]
    public async Task GetPdfAsync_BeforeIssued_ThrowsConflict()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);
        var created = await sut.CreateAsync(ValidCreate(f), Actor);

        var act = () => sut.GetPdfAsync(created.Id);

        var ex = await act.Should().ThrowAsync<InvoiceConflictException>();
        ex.Which.Extensions["currentStatus"].Should().Be("DRAFT");
    }

    [Fact]
    public async Task GetPdfAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        SeedMasterData(db);
        var sut = CreateSut(db, out _, out _, out _);

        var result = await sut.GetPdfAsync(Guid.NewGuid());

        result.Should().BeNull();
    }
}
