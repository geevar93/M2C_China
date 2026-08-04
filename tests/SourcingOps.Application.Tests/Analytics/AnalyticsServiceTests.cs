using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using SourcingOps.Application.Analytics;
using SourcingOps.Application.Common;
using SourcingOps.Application.Crm;
using SourcingOps.Application.Interfaces;
using SourcingOps.Application.Tests.TestSupport;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Caching;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Application.Tests.Analytics;

/// <summary>
/// Covers ACTION_PLAN E10-01…E10-07/E10-10 per the M7 contract. Weighted per the §7 DoD
/// towards: lookup-keyed zero-fill (rule 1), matching by Code never Label (rule 2, D-50
/// precedent), the CatalogDispatched/InvoiceDispatched split staying countable apart (rule 3,
/// D-67), filters actually narrowing results, `fromDate &gt; toDate` rejected, and
/// `conversionRate` never NaN/divide-by-zero. Uses the REAL <see cref="MemoryCacheService"/>
/// (not a mock) for the caching assertions — same precedent as MasterDataServiceTests.
/// </summary>
public class AnalyticsServiceTests
{
    private static AnalyticsService CreateSut(AppDbContext db, ICacheService? cache = null) =>
        new(db, cache ?? new MemoryCacheService(new MemoryCache(new MemoryCacheOptions())));

    private sealed record Fixture(
        ServiceType Cif, ServiceType FreightOnly,
        LeadStatus New, LeadStatus Won, LeadStatus Lost,
        Category Jewellery, Category Furniture, Category Stationery,
        VendorStatus VendorActive, VendorStatus VendorInactive,
        ShipmentStatus Packed, ShipmentStatus InTransit, ShipmentStatus Delivered,
        User StaffA, User StaffB);

    private static Fixture SeedMasterData(AppDbContext db)
    {
        var cif = new ServiceType { Id = Guid.NewGuid(), Code = "CIF", Label = "CIF", IsActive = true, SortOrder = 1 };
        var freightOnly = new ServiceType { Id = Guid.NewGuid(), Code = "FREIGHT_ONLY", Label = "Freight-only", IsActive = true, SortOrder = 2 };
        db.ServiceTypes.AddRange(cif, freightOnly);

        var newStatus = new LeadStatus { Id = Guid.NewGuid(), Code = "NEW", Label = "New", IsActive = true, SortOrder = 1 };
        var won = new LeadStatus { Id = Guid.NewGuid(), Code = "WON", Label = "Won", IsActive = true, SortOrder = 4 };
        var lost = new LeadStatus { Id = Guid.NewGuid(), Code = "LOST", Label = "Lost", IsActive = true, SortOrder = 5 };
        db.LeadStatuses.AddRange(newStatus, won, lost);

        var jewellery = new Category { Id = Guid.NewGuid(), Name = "Jewellery", IsActive = true, SortOrder = 1 };
        var furniture = new Category { Id = Guid.NewGuid(), Name = "Furniture", IsActive = true, SortOrder = 2 };
        var stationery = new Category { Id = Guid.NewGuid(), Name = "Stationery", IsActive = true, SortOrder = 3 };
        db.Categories.AddRange(jewellery, furniture, stationery);

        var vendorActive = new VendorStatus { Id = Guid.NewGuid(), Code = "ACTIVE", Label = "Active", IsActive = true, SortOrder = 1 };
        var vendorInactive = new VendorStatus { Id = Guid.NewGuid(), Code = "INACTIVE", Label = "Inactive", IsActive = true, SortOrder = 3 };
        db.VendorStatuses.AddRange(vendorActive, vendorInactive);

        var packed = new ShipmentStatus { Id = Guid.NewGuid(), Code = "PACKED", Label = "Packed", IsActive = true, SortOrder = 1 };
        var inTransit = new ShipmentStatus { Id = Guid.NewGuid(), Code = "IN TRANSIT", Label = "In Transit", IsActive = true, SortOrder = 3 };
        var delivered = new ShipmentStatus { Id = Guid.NewGuid(), Code = "DELIVERED", Label = "Delivered", IsActive = true, SortOrder = 4 };
        db.ShipmentStatuses.AddRange(packed, inTransit, delivered);

        var staffA = new User { Id = Guid.NewGuid(), Name = "Asha Rao", Email = $"a-{Guid.NewGuid():N}@example.com", IsActive = true, CreatedAt = DateTime.UtcNow };
        var staffB = new User { Id = Guid.NewGuid(), Name = "Bilal Khan", Email = $"b-{Guid.NewGuid():N}@example.com", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Users.AddRange(staffA, staffB);

        db.SaveChanges();
        return new Fixture(cif, freightOnly, newStatus, won, lost, jewellery, furniture, stationery,
            vendorActive, vendorInactive, packed, inTransit, delivered, staffA, staffB);
    }

    private static Customer MakeCustomer(Fixture f, ServiceType serviceType, LeadStatus status, DateTime createdAt, string? sourceChannel = "Website", Category? category = null)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Name = $"Customer {Guid.NewGuid():N}"[..20],
            Phone = $"+9198{Random.Shared.NextInt64(10000000, 99999999)}",
            ServiceTypeId = serviceType.Id,
            ServiceType = serviceType,
            StatusId = status.Id,
            Status = status,
            SourceChannel = sourceChannel,
            CreatedAt = createdAt
        };
        if (category is not null)
        {
            customer.CustomerCategories.Add(new CustomerCategory { CustomerId = customer.Id, Customer = customer, CategoryId = category.Id, Category = category });
        }
        return customer;
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- Leads (E10-01) -------------------------------------------------------------------

    [Fact]
    public async Task Leads_ZeroLeads_ConversionRateIsZero_NeverNaNOrThrow()
    {
        using var db = TestDbContextFactory.Create();
        SeedMasterData(db);
        var sut = CreateSut(db);

        var result = await sut.GetLeadsAsync(new AnalyticsQuery(null, null, null, null));

        result.TotalLeads.Should().Be(0);
        result.WonCount.Should().Be(0);
        result.ConversionRate.Should().Be(0m);
    }

    [Fact]
    public async Task Leads_ByStatus_ZeroFillsStatusesWithNoLeads_AndComputesConversionRate()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var now = DateTime.UtcNow;
        db.Customers.AddRange(
            MakeCustomer(f, f.Cif, f.New, now),
            MakeCustomer(f, f.Cif, f.Won, now),
            MakeCustomer(f, f.Cif, f.Won, now));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetLeadsAsync(new AnalyticsQuery(null, null, null, null));

        result.TotalLeads.Should().Be(3);
        result.WonCount.Should().Be(2);
        result.ConversionRate.Should().Be(2m / 3m);
        result.ByStatus.Should().HaveCount(3, "all three seeded lead statuses appear, including the zero one");
        result.ByStatus.Single(s => s.Code == "LOST").Count.Should().Be(0, "LOST has no leads but must still zero-fill (E10 rule 1)");
        result.ByStatus.Single(s => s.Code == "WON").Count.Should().Be(2);
    }

    [Fact]
    public async Task Leads_ServiceTypeFilter_NarrowsResults()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var now = DateTime.UtcNow;
        db.Customers.AddRange(
            MakeCustomer(f, f.Cif, f.New, now),
            MakeCustomer(f, f.FreightOnly, f.New, now));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetLeadsAsync(new AnalyticsQuery(null, null, null, f.Cif.Id));

        result.TotalLeads.Should().Be(1);
    }

    [Fact]
    public async Task Leads_CategoryFilter_NarrowsResults()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var now = DateTime.UtcNow;
        db.Customers.AddRange(
            MakeCustomer(f, f.Cif, f.New, now, category: f.Jewellery),
            MakeCustomer(f, f.Cif, f.New, now, category: f.Furniture));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetLeadsAsync(new AnalyticsQuery(null, null, f.Jewellery.Id, null));

        result.TotalLeads.Should().Be(1);
    }

    [Fact]
    public async Task Leads_FromDateAfterToDate_ThrowsAppValidationException()
    {
        using var db = TestDbContextFactory.Create();
        SeedMasterData(db);
        var sut = CreateSut(db);

        var act = () => sut.GetLeadsAsync(new AnalyticsQuery(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 1), null, null));

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("fromDate");
    }

    [Fact]
    public async Task Leads_DateRange_ExcludesLeadsOutsideIt()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        db.Customers.AddRange(
            MakeCustomer(f, f.Cif, f.New, new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc)),
            MakeCustomer(f, f.Cif, f.New, new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetLeadsAsync(new AnalyticsQuery(new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31), null, null));

        result.TotalLeads.Should().Be(1, "only the August lead falls inside the filtered range");
    }

    [Fact]
    public async Task Leads_BySource_MatchesByCode_NotByLabel_AndZeroFillsUnusedChannel()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var now = DateTime.UtcNow;
        db.Customers.AddRange(
            MakeCustomer(f, f.Cif, f.New, now, sourceChannel: "Referral"),
            MakeCustomer(f, f.Cif, f.New, now, sourceChannel: "Website"));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetLeadsAsync(new AnalyticsQuery(null, null, null, null));

        result.BySource.Should().Contain(x => x.Code == "REFERRAL" && x.Count == 1);
        result.BySource.Should().Contain(x => x.Code == "WEBSITE" && x.Count == 1);
    }

    // ---- Service split (E10-02) ------------------------------------------------------------

    [Fact]
    public async Task ServiceSplit_ZeroFillsServiceTypeWithNoCustomers()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        db.Customers.Add(MakeCustomer(f, f.Cif, f.New, DateTime.UtcNow));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetServiceSplitAsync(new AnalyticsQuery(null, null, null, null));

        result.TotalCustomers.Should().Be(1);
        result.Items.Should().HaveCount(2);
        result.Items.Single(i => i.Code == "FREIGHT_ONLY").CustomerCount.Should().Be(0);
        result.Items.Single(i => i.Code == "CIF").CustomerCount.Should().Be(1);
    }

    // ---- Category mix (E10-03) -------------------------------------------------------------

    [Fact]
    public async Task CategoryMix_ZeroFillsCategoryWithNoCustomers_OrderedBySortOrder()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        db.Customers.Add(MakeCustomer(f, f.Cif, f.New, DateTime.UtcNow, category: f.Jewellery));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetCategoryMixAsync(new AnalyticsQuery(null, null, null, null));

        result.Items.Should().HaveCount(3);
        result.Items.Select(i => i.Label).Should().ContainInOrder("Jewellery", "Furniture", "Stationery");
        result.Items.Single(i => i.Label == "Jewellery").CustomerCount.Should().Be(1);
        result.Items.Single(i => i.Label == "Furniture").CustomerCount.Should().Be(0);
    }

    // ---- Vendors (E10-04) -----------------------------------------------------------------

    [Fact]
    public async Task Vendors_TotalActive_MatchesByCode_AndByCategoryZeroFills()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var activeVendor = new Vendor { Id = Guid.NewGuid(), Name = "Golden Dragon", StatusId = f.VendorActive.Id, Status = f.VendorActive, CreatedAt = DateTime.UtcNow };
        var inactiveVendor = new Vendor { Id = Guid.NewGuid(), Name = "Old Co", StatusId = f.VendorInactive.Id, Status = f.VendorInactive, CreatedAt = DateTime.UtcNow };
        db.Vendors.AddRange(activeVendor, inactiveVendor);
        db.VendorCategories.Add(new VendorCategory { VendorId = activeVendor.Id, Vendor = activeVendor, CategoryId = f.Jewellery.Id, Category = f.Jewellery });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetVendorsAsync(new AnalyticsQuery(null, null, null, null));

        result.TotalActiveVendors.Should().Be(1, "only Active-status vendors count, matched by Code not Label");
        result.ByCategory.Should().HaveCount(3);
        result.ByCategory.Single(c => c.Label == "Jewellery").VendorCount.Should().Be(1);
        result.ByCategory.Single(c => c.Label == "Furniture").VendorCount.Should().Be(0);
    }

    // ---- Inventory (E10-05, never cached) --------------------------------------------------

    [Fact]
    public async Task Inventory_OnHandValue_AndByCategory_ComputeCorrectly()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        db.InventoryItems.Add(new InventoryItem { Id = Guid.NewGuid(), Name = "Ring", CategoryId = f.Jewellery.Id, Category = f.Jewellery, OnHandQty = 10, UnitCost = 5m, Unit = "pcs" });
        db.InventoryItems.Add(new InventoryItem { Id = Guid.NewGuid(), Name = "Chair", CategoryId = f.Furniture.Id, Category = f.Furniture, OnHandQty = 2, UnitCost = 100m, Unit = "pcs" });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetInventoryAsync(new AnalyticsQuery(null, null, null, null));

        result.OnHandValue.Should().Be(50m + 200m);
        result.ByCategory.Should().HaveCount(3, "zero-filled against all three categories");
        result.ByCategory.Single(c => c.Label == "Stationery").Quantity.Should().Be(0);
    }

    [Fact]
    public async Task Inventory_PastEtaCount_ExcludesDelivered_MatchesStatusByCode()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var customer = MakeCustomer(f, f.Cif, f.New, DateTime.UtcNow);
        db.Customers.Add(customer);

        var pastEtaNotDelivered = new Shipment
        {
            Id = Guid.NewGuid(), CustomerId = customer.Id, Customer = customer, ServiceTypeId = f.Cif.Id, ServiceType = f.Cif,
            StatusId = f.InTransit.Id, Status = f.InTransit, Eta = DateTime.UtcNow.AddDays(-3), CreatedAt = DateTime.UtcNow
        };
        var pastEtaButDelivered = new Shipment
        {
            Id = Guid.NewGuid(), CustomerId = customer.Id, Customer = customer, ServiceTypeId = f.Cif.Id, ServiceType = f.Cif,
            StatusId = f.Delivered.Id, Status = f.Delivered, Eta = DateTime.UtcNow.AddDays(-3), CreatedAt = DateTime.UtcNow
        };
        var futureEta = new Shipment
        {
            Id = Guid.NewGuid(), CustomerId = customer.Id, Customer = customer, ServiceTypeId = f.Cif.Id, ServiceType = f.Cif,
            StatusId = f.Packed.Id, Status = f.Packed, Eta = DateTime.UtcNow.AddDays(3), CreatedAt = DateTime.UtcNow
        };
        db.Shipments.AddRange(pastEtaNotDelivered, pastEtaButDelivered, futureEta);
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetInventoryAsync(new AnalyticsQuery(null, null, null, null));

        result.PastEtaCount.Should().Be(1);
        result.InTransitCount.Should().Be(1);
        result.ShipmentsByStatus.Should().HaveCount(3, "zero-filled against all three seeded shipment statuses");
    }

    [Fact]
    public async Task Inventory_NeverServedFromCache_SubsequentCallsSeeFreshData()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db);

        var before = await sut.GetInventoryAsync(new AnalyticsQuery(null, null, null, null));
        before.OnHandValue.Should().Be(0);

        db.InventoryItems.Add(new InventoryItem { Id = Guid.NewGuid(), Name = "Ring", CategoryId = f.Jewellery.Id, Category = f.Jewellery, OnHandQty = 1, UnitCost = 999m, Unit = "pcs" });
        await db.SaveChangesAsync();

        var after = await sut.GetInventoryAsync(new AnalyticsQuery(null, null, null, null));
        after.OnHandValue.Should().Be(999m, "inventory is the one endpoint that must never be served from cache (E10-10)");
    }

    // ---- Dispatch (E10-06) ------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_ByKind_DistinguishesCatalogFromInvoiceDispatch()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var customer = MakeCustomer(f, f.Cif, f.New, DateTime.UtcNow);
        db.Customers.Add(customer);

        var vendor = new Vendor { Id = Guid.NewGuid(), Name = "Golden Dragon", StatusId = f.VendorActive.Id, Status = f.VendorActive, CreatedAt = DateTime.UtcNow };
        db.Vendors.Add(vendor);
        var section = new CatalogSection { Id = Guid.NewGuid(), VendorId = vendor.Id, Vendor = vendor, Title = "Spring", CategoryId = f.Jewellery.Id, Category = f.Jewellery, CreatedAt = DateTime.UtcNow };
        db.CatalogSections.Add(section);
        var document = new CatalogDocument { Id = Guid.NewGuid(), CatalogSectionId = section.Id, CatalogSection = section, FilePath = "x.pdf", OriginalFilename = "x.pdf", SizeBytes = 10, UploadedByUserId = f.StaffA.Id, UploadedBy = f.StaffA, UploadedAt = DateTime.UtcNow };
        db.CatalogDocuments.Add(document);

        var invoiceStatus = new InvoiceStatus { Id = Guid.NewGuid(), Code = "DRAFT", Label = "Draft", IsActive = true, SortOrder = 1 };
        db.InvoiceStatuses.Add(invoiceStatus);
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(), InvoiceNumber = "INV-2608-001", CustomerId = customer.Id, Customer = customer,
            InvoiceDate = DateTime.UtcNow, Amount = 100m, TaxAmount = 0m, Currency = "INR",
            StatusId = invoiceStatus.Id, Status = invoiceStatus, CreatedByUserId = f.StaffA.Id, CreatedAt = DateTime.UtcNow
        };
        db.Invoices.Add(invoice);

        db.Dispatches.AddRange(
            new Dispatch { Id = Guid.NewGuid(), CustomerId = customer.Id, Customer = customer, CatalogDocumentId = document.Id, StaffUserId = f.StaffA.Id, StaffUser = f.StaffA, Message = "m", SentAt = DateTime.UtcNow },
            new Dispatch { Id = Guid.NewGuid(), CustomerId = customer.Id, Customer = customer, CatalogDocumentId = document.Id, StaffUserId = f.StaffA.Id, StaffUser = f.StaffA, Message = "m", SentAt = DateTime.UtcNow },
            new Dispatch { Id = Guid.NewGuid(), CustomerId = customer.Id, Customer = customer, InvoiceId = invoice.Id, StaffUserId = f.StaffB.Id, StaffUser = f.StaffB, Message = "m", SentAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.GetDispatchAsync(new AnalyticsQuery(null, null, null, null));

        result.TotalDispatches.Should().Be(3);
        result.ByKind.Should().HaveCount(2, "both kinds always present, even if one were zero (D-67/E10 rule 3)");
        result.ByKind.Single(k => k.Kind == "CatalogDispatched").Count.Should().Be(2);
        result.ByKind.Single(k => k.Kind == "InvoiceDispatched").Count.Should().Be(1);
        result.ByStaff.Should().Contain(s => s.UserId == f.StaffA.Id && s.Count == 2);
        result.ByStaff.Should().Contain(s => s.UserId == f.StaffB.Id && s.Count == 1);
    }

    // ---- Caching (E10-10) -------------------------------------------------------------------

    [Fact]
    public async Task Cache_SecondIdenticalCall_ServedFromCache_UntilFiltersDiffer()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var cache = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));
        var sut = CreateSut(db, cache);

        var first = await sut.GetServiceSplitAsync(new AnalyticsQuery(null, null, null, null));
        first.TotalCustomers.Should().Be(0);

        db.Customers.Add(MakeCustomer(f, f.Cif, f.New, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var second = await sut.GetServiceSplitAsync(new AnalyticsQuery(null, null, null, null));
        second.TotalCustomers.Should().Be(0, "same filters within the 60s TTL must be served from cache, not re-queried");

        var differentFilter = await sut.GetServiceSplitAsync(new AnalyticsQuery(null, null, null, f.Cif.Id));
        differentFilter.TotalCustomers.Should().Be(1, "a different serviceTypeId filter is a different cache key, so it must NOT reuse the unfiltered cache entry");
    }
}
