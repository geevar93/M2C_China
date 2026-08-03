using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SourcingOps.Application.Common;
using SourcingOps.Application.Crm;
using SourcingOps.Application.Interfaces;
using SourcingOps.Application.Tests.TestSupport;
using SourcingOps.Domain.Constants;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Application.Tests.Crm;

/// <summary>
/// Covers ACTION_PLAN E4-01…E4-12 against the coordinator's binding `/api/v1/customers`
/// contract. Uses the EF Core InMemory provider (see <see cref="TestDbContextFactory"/>) —
/// <see cref="CustomerService"/> deliberately materializes list pages before mapping (same
/// defensive pattern as <c>MasterDataService</c>), so this suite exercises the exact same
/// query shape the real Npgsql provider sees.
/// </summary>
public class CustomerServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private static CustomerService CreateSut(AppDbContext db, out Mock<IAuditLogger> auditMock, string defaultCountryCode = "+91")
    {
        auditMock = new Mock<IAuditLogger>();
        var options = new CustomerOptions { DefaultCountryPhoneCode = defaultCountryCode };
        return new CustomerService(db, auditMock.Object, options);
    }

    private sealed record Fixture(ServiceType Cif, ServiceType FreightOnly, LeadStatus New, LeadStatus Qualified, Category Jewellery, Category Furniture, User Owner);

    private static Fixture SeedMasterData(AppDbContext db)
    {
        var cif = new ServiceType { Id = Guid.NewGuid(), Code = SeedDefaults.ServiceTypeCif, Label = "CIF", IsActive = true, SortOrder = 1 };
        var freightOnly = new ServiceType { Id = Guid.NewGuid(), Code = SeedDefaults.ServiceTypeFreightOnly, Label = "Freight-only", IsActive = true, SortOrder = 2 };
        db.ServiceTypes.AddRange(cif, freightOnly);

        var newStatus = new LeadStatus { Id = Guid.NewGuid(), Code = "NEW", Label = "New", IsActive = true, SortOrder = 1 };
        var qualified = new LeadStatus { Id = Guid.NewGuid(), Code = "QUALIFIED", Label = "Qualified", IsActive = true, SortOrder = 2 };
        db.LeadStatuses.AddRange(newStatus, qualified);

        var jewellery = new Category { Id = Guid.NewGuid(), Name = "Jewellery", IsActive = true, SortOrder = 1 };
        var furniture = new Category { Id = Guid.NewGuid(), Name = "Furniture", IsActive = true, SortOrder = 2 };
        db.Categories.AddRange(jewellery, furniture);

        var owner = new User { Id = Guid.NewGuid(), Name = "Priya Sharma", Email = $"owner-{Guid.NewGuid():N}@example.com", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Users.Add(owner);

        // Interaction.AuthorUserId is a real, Restrict-on-delete FK (never orphaned in
        // production, since Postgres enforces it) — Interaction.Author is a REQUIRED
        // navigation, and the EF Core InMemory provider silently drops a row from an
        // `Include(...)` result when the referenced entity doesn't exist. Every mutating
        // CustomerService call in this suite passes `Actor` as actorUserId, and several of
        // those calls create Interaction rows (EnquiryCaptured/StatusChange/OwnerChanged/
        // manual notes) — so `Actor` must resolve to a real seeded User here, or
        // GetTimelineAsync/GetDueFollowUpsAsync's Include(i => i.Author) silently returns an
        // empty collection instead of throwing, which is exactly the failure mode this
        // comment is here to prevent recurring.
        db.Users.Add(new User { Id = Actor, Name = "Acting Staff", Email = $"actor-{Guid.NewGuid():N}@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });

        db.SaveChanges();
        return new Fixture(cif, freightOnly, newStatus, qualified, jewellery, furniture, owner);
    }

    private static CreateCustomerRequest ValidCifRequest(Fixture f, string phone = "9825041122") => new(
        Name: "Kundan Traders",
        BusinessName: "Kundan Traders Pvt Ltd",
        Phone: phone,
        Email: "kundan@example.com",
        City: "Surat",
        Region: "Gujarat",
        SourceChannel: "WhatsApp",
        ServiceTypeId: f.Cif.Id,
        StatusId: f.New.Id,
        CategoryIds: [f.Jewellery.Id],
        OwnerUserId: f.Owner.Id,
        Tags: ["VIP"],
        Notes: "Interested in bridal sets.",
        ExternalMarketplace: null,
        ExternalOrderRef: null,
        ExternalSupplierName: null,
        ExternalOrderValue: null,
        ExternalOrderCurrency: null,
        ExternalOrderDate: null);

    // ---- Create (E4-01…E4-05) --------------------------------------------------

    [Fact]
    public async Task CreateAsync_ValidCifCustomer_CreatesAndReturnsDetail_WithEnquiryCapturedOnTimeline()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit);

        var outcome = await sut.CreateAsync(ValidCifRequest(f), Actor);

        outcome.DuplicateExisting.Should().BeNull();
        outcome.Created.Should().NotBeNull();
        outcome.Created!.Name.Should().Be("Kundan Traders");
        outcome.Created.Phone.Should().Be("+919825041122");
        outcome.Created.ServiceTypeId.Should().Be(f.Cif.Id);
        outcome.Created.StatusId.Should().Be(f.New.Id);
        outcome.Created.CategoryIds.Should().Equal(f.Jewellery.Id);
        outcome.Created.OwnerUserId.Should().Be(f.Owner.Id);
        outcome.Created.OwnerName.Should().Be("Priya Sharma");
        outcome.Created.Tags.Should().Equal("VIP");

        var timeline = await sut.GetTimelineAsync(outcome.Created.Id);
        timeline.Should().ContainSingle(e => e.Kind == TimelineEventKinds.EnquiryCaptured);

        audit.Verify(a => a.LogAsync(Actor, "CustomerCreated", "Customer", outcome.Created.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_PhoneIsNormalizedToE164()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var outcome = await sut.CreateAsync(ValidCifRequest(f, phone: "098250-41122"), Actor);

        outcome.Created!.Phone.Should().Be("+919825041122");
    }

    [Fact]
    public async Task CreateAsync_BlankName_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var request = ValidCifRequest(f) with { Name = "  " };

        var act = async () => await sut.CreateAsync(request, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task CreateAsync_UnknownServiceTypeId_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var request = ValidCifRequest(f) with { ServiceTypeId = Guid.NewGuid() };

        var act = async () => await sut.CreateAsync(request, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task CreateAsync_UnknownCategoryId_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var request = ValidCifRequest(f) with { CategoryIds = [Guid.NewGuid()] };

        var act = async () => await sut.CreateAsync(request, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task CreateAsync_InactiveOwner_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        f.Owner.IsActive = false;
        await db.SaveChangesAsync();
        var sut = CreateSut(db, out _);
        var request = ValidCifRequest(f) with { OwnerUserId = f.Owner.Id };

        var act = async () => await sut.CreateAsync(request, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task CreateAsync_OmittedStatusId_DefaultsToLowestSortOrderActiveStatus()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var request = ValidCifRequest(f) with { StatusId = null };

        var outcome = await sut.CreateAsync(request, Actor);

        outcome.Created!.StatusId.Should().Be(f.New.Id); // SortOrder 1, lower than Qualified's 2
    }

    // ---- E4-12: external-purchase fields only valid for freight-only ------------

    [Fact]
    public async Task CreateAsync_FreightOnlyWithExternalPurchaseFields_Succeeds()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var request = ValidCifRequest(f) with
        {
            ServiceTypeId = f.FreightOnly.Id,
            ExternalMarketplace = "Alibaba",
            ExternalOrderRef = "ALI-12345",
            ExternalSupplierName = "Shenzhen Supplier Co",
            ExternalOrderValue = 1500.50m,
            ExternalOrderCurrency = "usd",
            ExternalOrderDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var outcome = await sut.CreateAsync(request, Actor);

        outcome.Created.Should().NotBeNull();
        outcome.Created!.ExternalMarketplace.Should().Be("Alibaba");
        outcome.Created.ExternalOrderCurrency.Should().Be("USD"); // upper-cased for ISO 4217 consistency
    }

    [Fact]
    public async Task CreateAsync_ExternalPurchaseFieldsOnCifCustomer_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var request = ValidCifRequest(f) with { ExternalMarketplace = "Alibaba" }; // still CIF

        var act = async () => await sut.CreateAsync(request, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    // ---- E4-10: duplicate phone -------------------------------------------------

    [Fact]
    public async Task CreateAsync_DuplicatePhone_WithoutConfirm_ReturnsDuplicateOutcome_AndDoesNotCreateASecondRecord()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var first = await sut.CreateAsync(ValidCifRequest(f), Actor);

        var second = await sut.CreateAsync(ValidCifRequest(f) with { Name = "Different Name" }, Actor);

        second.Created.Should().BeNull();
        second.DuplicateExisting.Should().NotBeNull();
        second.DuplicateExisting!.Id.Should().Be(first.Created!.Id);
        (await db.Customers.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_DuplicatePhone_WithConfirmDuplicateTrue_CreatesAnyway()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        await sut.CreateAsync(ValidCifRequest(f), Actor);

        var second = await sut.CreateAsync(ValidCifRequest(f) with { Name = "Different Name", ConfirmDuplicate = true }, Actor);

        second.Created.Should().NotBeNull();
        second.DuplicateExisting.Should().BeNull();
        (await db.Customers.CountAsync()).Should().Be(2);
    }

    // ---- Update -------------------------------------------------------------------

    private static UpdateCustomerRequest UpdateRequestFrom(CustomerDetailDto d, Guid serviceTypeId, Guid statusId, IReadOnlyList<Guid>? categoryIds = null) => new(
        Name: d.Name,
        BusinessName: d.BusinessName,
        Phone: d.Phone,
        Email: d.Email,
        City: d.City,
        Region: d.Region,
        SourceChannel: d.SourceChannel,
        ServiceTypeId: serviceTypeId,
        StatusId: statusId,
        CategoryIds: categoryIds ?? d.CategoryIds,
        Tags: d.Tags,
        Notes: d.Notes,
        ExternalMarketplace: d.ExternalMarketplace,
        ExternalOrderRef: d.ExternalOrderRef,
        ExternalSupplierName: d.ExternalSupplierName,
        ExternalOrderValue: d.ExternalOrderValue,
        ExternalOrderCurrency: d.ExternalOrderCurrency,
        ExternalOrderDate: d.ExternalOrderDate);

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var request = new UpdateCustomerRequest(
            Name: "X", BusinessName: null, Phone: "9000000000", Email: null, City: null, Region: null, SourceChannel: null,
            ServiceTypeId: f.Cif.Id, StatusId: f.New.Id, CategoryIds: null, Tags: null, Notes: null,
            ExternalMarketplace: null, ExternalOrderRef: null, ExternalSupplierName: null,
            ExternalOrderValue: null, ExternalOrderCurrency: null, ExternalOrderDate: null);

        var result = await sut.UpdateAsync(Guid.NewGuid(), request, Actor);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_StatusChange_AddsStatusChangedTimelineEntry()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor);

        var updateRequest = UpdateRequestFrom(created.Created!, f.Cif.Id, f.Qualified.Id);

        var updated = await sut.UpdateAsync(created.Created!.Id, updateRequest, Actor);

        updated!.StatusId.Should().Be(f.Qualified.Id);
        var timeline = await sut.GetTimelineAsync(created.Created.Id);
        timeline.Should().ContainSingle(e => e.Kind == TimelineEventKinds.StatusChanged);
        audit.Verify(a => a.LogAsync(Actor, "CustomerUpdated", "Customer", created.Created.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_NoStatusChange_DoesNotAddAnExtraTimelineEntry()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor);

        var updateRequest = UpdateRequestFrom(created.Created!, f.Cif.Id, f.New.Id /* unchanged */);

        await sut.UpdateAsync(created.Created!.Id, updateRequest, Actor);

        var timeline = await sut.GetTimelineAsync(created.Created.Id);
        timeline.Should().NotContain(e => e.Kind == TimelineEventKinds.StatusChanged);
        timeline.Should().ContainSingle(e => e.Kind == TimelineEventKinds.EnquiryCaptured);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotChangeOwner_EvenIfCallerTriesViaOtherFields()
    {
        // UpdateCustomerRequest structurally has no OwnerUserId — owner changes must go
        // through ChangeOwnerAsync (E4-09). This test documents/proves that Update leaves
        // the owner untouched.
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor); // owned by f.Owner

        var updateRequest = UpdateRequestFrom(created.Created!, f.Cif.Id, f.New.Id);
        var updated = await sut.UpdateAsync(created.Created!.Id, updateRequest, Actor);

        updated!.OwnerUserId.Should().Be(f.Owner.Id);
    }

    // ---- Owner assignment (E4-09) --------------------------------------------------

    [Fact]
    public async Task ChangeOwnerAsync_ReassignsOwner_AddsTimelineEntry_AndAudits()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit);
        var created = await sut.CreateAsync(ValidCifRequest(f) with { OwnerUserId = null }, Actor);

        var updated = await sut.ChangeOwnerAsync(created.Created!.Id, f.Owner.Id, Actor);

        updated!.OwnerUserId.Should().Be(f.Owner.Id);
        updated.OwnerName.Should().Be("Priya Sharma");
        var timeline = await sut.GetTimelineAsync(created.Created.Id);
        timeline.Should().ContainSingle(e => e.Kind == TimelineEventKinds.OwnerChanged);
        audit.Verify(a => a.LogAsync(Actor, "CustomerOwnerChanged", "Customer", created.Created.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }

    [Fact]
    public async Task ChangeOwnerAsync_ToNull_UnassignsOwner()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor); // owned by f.Owner

        var updated = await sut.ChangeOwnerAsync(created.Created!.Id, null, Actor);

        updated!.OwnerUserId.Should().BeNull();
        updated.OwnerName.Should().BeNull();
    }

    [Fact]
    public async Task ChangeOwnerAsync_SameOwner_IsANoOp_DoesNotAddATimelineEntry()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor); // already owned by f.Owner

        await sut.ChangeOwnerAsync(created.Created!.Id, f.Owner.Id, Actor);

        var timeline = await sut.GetTimelineAsync(created.Created.Id);
        timeline.Should().NotContain(e => e.Kind == TimelineEventKinds.OwnerChanged);
        audit.Verify(a => a.LogAsync(It.IsAny<Guid?>(), "CustomerOwnerChanged", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), default), Times.Never);
    }

    [Fact]
    public async Task ChangeOwnerAsync_UnknownCustomer_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);

        var result = await sut.ChangeOwnerAsync(Guid.NewGuid(), f.Owner.Id, Actor);

        result.Should().BeNull();
    }

    // ---- Interactions / notes (E4-07) ----------------------------------------------

    [Fact]
    public async Task AddInteractionAsync_ValidNote_IsAddedAndAppearsOnTimelineAsNoteAdded()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor);

        var interaction = await sut.AddInteractionAsync(created.Created!.Id, new CreateInteractionRequest("Note", "Called, follow up next week.", null), Actor);

        interaction.Should().NotBeNull();
        interaction!.AuthorUserId.Should().Be(Actor);
        var timeline = await sut.GetTimelineAsync(created.Created.Id);
        timeline.Should().ContainSingle(e => e.Kind == TimelineEventKinds.NoteAdded && e.Body == "Called, follow up next week.");
    }

    [Fact]
    public async Task AddInteractionAsync_OmittedType_DefaultsToNote()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor);

        var interaction = await sut.AddInteractionAsync(created.Created!.Id, new CreateInteractionRequest(null, "A note.", null), Actor);

        interaction!.Type.Should().Be("Note");
    }

    [Theory]
    [InlineData("StatusChange")]
    [InlineData("OwnerChanged")]
    [InlineData("EnquiryCaptured")]
    [InlineData("statuschange")] // case-insensitive
    public async Task AddInteractionAsync_ReservedSystemType_ThrowsValidationException(string reservedType)
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor);

        var act = async () => await sut.AddInteractionAsync(created.Created!.Id, new CreateInteractionRequest(reservedType, "Fake status change.", null), Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task AddInteractionAsync_BlankText_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor);

        var act = async () => await sut.AddInteractionAsync(created.Created!.Id, new CreateInteractionRequest("Note", "   ", null), Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task AddInteractionAsync_UnknownCustomer_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var result = await sut.AddInteractionAsync(Guid.NewGuid(), new CreateInteractionRequest("Note", "Text", null), Actor);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AddInteractionAsync_WithFollowUpDate_SurfacesInDueFollowUps_WhenAsOfIsAtOrAfterIt()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor);
        var followUpDate = DateTime.UtcNow.AddDays(-1);

        await sut.AddInteractionAsync(created.Created!.Id, new CreateInteractionRequest("Call", "Call back", followUpDate), Actor);

        var due = await sut.GetDueFollowUpsAsync(DateTime.UtcNow);
        due.Should().ContainSingle(d => d.CustomerId == created.Created.Id && d.CustomerName == "Kundan Traders");
    }

    [Fact]
    public async Task GetDueFollowUpsAsync_FutureFollowUp_IsNotYetDue()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor);
        var futureDate = DateTime.UtcNow.AddDays(7);
        await sut.AddInteractionAsync(created.Created!.Id, new CreateInteractionRequest("Call", "Call back next week", futureDate), Actor);

        var due = await sut.GetDueFollowUpsAsync(DateTime.UtcNow);

        due.Should().BeEmpty();
    }

    // ---- Timeline (E4-07) -----------------------------------------------------------

    [Fact]
    public async Task GetTimelineAsync_ReturnsNewestFirst()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor);
        await sut.AddInteractionAsync(created.Created!.Id, new CreateInteractionRequest("Note", "First note", null), Actor);
        await sut.AddInteractionAsync(created.Created.Id, new CreateInteractionRequest("Note", "Second note", null), Actor);

        var timeline = await sut.GetTimelineAsync(created.Created.Id);

        timeline!.Select(e => e.Body).Should().ContainInOrder("Second note", "First note", "New CIF enquiry captured via WhatsApp.");
    }

    [Fact]
    public async Task GetTimelineAsync_UnknownCustomer_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var result = await sut.GetTimelineAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetTimelineAsync_OccurredAtUtc_CarriesUtcKind()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor);

        var timeline = await sut.GetTimelineAsync(created.Created!.Id);

        timeline!.Single().OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    // ---- Timeline: M4/E9 dispatch merge --------------------------------------------------

    /// <summary>Minimal catalog fixture for seeding a <see cref="Dispatch"/> row directly (mirrors DispatchServiceTests.SeedData).</summary>
    private static CatalogDocument SeedCatalogDocument(AppDbContext db, User staff)
    {
        var vendorStatus = new VendorStatus { Id = Guid.NewGuid(), Code = "ACTIVE", Label = "Active", IsActive = true, SortOrder = 1 };
        var category = new Category { Id = Guid.NewGuid(), Name = "Jewellery", IsActive = true, SortOrder = 1 };
        var vendor = new Vendor { Id = Guid.NewGuid(), Name = "Golden Dragon Manufacturing", StatusId = vendorStatus.Id, CreatedAt = DateTime.UtcNow };
        var section = new CatalogSection
        {
            Id = Guid.NewGuid(), VendorId = vendor.Id, Vendor = vendor, Title = "Spring 2026 Collection",
            CategoryId = category.Id, Category = category, CreatedAt = DateTime.UtcNow
        };
        var document = new CatalogDocument
        {
            Id = Guid.NewGuid(), CatalogSectionId = section.Id, CatalogSection = section,
            FilePath = "catalog-docs/x/y.pdf", OriginalFilename = "spring-2026.pdf", SizeBytes = 1024,
            IsLatest = true, UploadedByUserId = staff.Id, UploadedBy = staff, UploadedAt = DateTime.UtcNow
        };
        db.VendorStatuses.Add(vendorStatus);
        db.Categories.Add(category);
        db.Vendors.Add(vendor);
        db.CatalogSections.Add(section);
        db.CatalogDocuments.Add(document);
        db.SaveChanges();
        return document;
    }

    [Fact]
    public async Task GetTimelineAsync_IncludesDispatchedCatalogEvent_WithCatalogAndDocumentNameInBody_AndCatalogDocumentRef()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var staff = db.Users.Single(u => u.Id == Actor);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor);
        var document = SeedCatalogDocument(db, staff);
        db.Dispatches.Add(new Dispatch
        {
            Id = Guid.NewGuid(), CustomerId = created.Created!.Id, CatalogDocumentId = document.Id,
            StaffUserId = Actor, Message = "Hi, here is our catalog.", SentAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var timeline = await sut.GetTimelineAsync(created.Created.Id);

        var dispatchEvent = timeline!.Single(e => e.Kind == TimelineEventKinds.CatalogDispatched);
        dispatchEvent.Body.Should().Contain("Spring 2026 Collection").And.Contain("spring-2026.pdf");
        dispatchEvent.RefType.Should().Be("CatalogDocument");
        dispatchEvent.RefId.Should().Be(document.Id);
        dispatchEvent.ActorUserId.Should().Be(Actor);
        dispatchEvent.ActorName.Should().Be("Acting Staff");
    }

    [Fact]
    public async Task GetTimelineAsync_DispatchInterleavedBetweenTwoInteractions_MergesIntoTrueChronologicalOrder()
    {
        // The regression this guards against: sorting each source independently before
        // concatenating (interactions desc, then appending dispatches) would place every
        // dispatch at the tail no matter its actual timestamp — this only fails when a
        // dispatch's SentAt falls strictly BETWEEN two interaction timestamps, which is
        // exactly what this test constructs.
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var staff = db.Users.Single(u => u.Id == Actor);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor);
        var customerId = created.Created!.Id;
        var document = SeedCatalogDocument(db, staff);

        // Wipe the auto-generated EnquiryCaptured interaction's timestamp noise by controlling
        // all three events' timestamps explicitly and directly.
        var t1 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc); // dispatch — must land strictly between t1 and t3
        var t3 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var earliestInteraction = db.Interactions.Single(i => i.CustomerId == customerId);
        earliestInteraction.CreatedAt = t1;

        db.Interactions.Add(new Interaction
        {
            Id = Guid.NewGuid(), CustomerId = customerId, AuthorUserId = Actor,
            Type = "Note", Text = "Latest note", CreatedAt = t3
        });
        db.Dispatches.Add(new Dispatch
        {
            Id = Guid.NewGuid(), CustomerId = customerId, CatalogDocumentId = document.Id,
            StaffUserId = Actor, Message = "Hi, here is our catalog.", SentAt = t2
        });
        await db.SaveChangesAsync();

        var timeline = await sut.GetTimelineAsync(customerId);

        timeline!.Select(e => e.Kind).Should().ContainInOrder(
            TimelineEventKinds.NoteAdded, TimelineEventKinds.CatalogDispatched, TimelineEventKinds.EnquiryCaptured);
        timeline!.Select(e => e.OccurredAtUtc).Should().BeInDescendingOrder();
    }

    // ---- List / search / filter (E4-06) ----------------------------------------------

    [Fact]
    public async Task ListAsync_FiltersByEveryContractParameter()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var matching = await sut.CreateAsync(ValidCifRequest(f, phone: "9000000001"), Actor);
        await sut.CreateAsync(ValidCifRequest(f, phone: "9000000002") with { ServiceTypeId = f.FreightOnly.Id, CategoryIds = [f.Furniture.Id], Region = "Delhi", OwnerUserId = null, Tags = ["Other"] }, Actor);

        var byStatus = await sut.ListAsync(new CustomerListQuery(null, 1, 25, f.New.Id, null, null, null, null, null));
        var byServiceType = await sut.ListAsync(new CustomerListQuery(null, 1, 25, null, f.Cif.Id, null, null, null, null));
        var byCategory = await sut.ListAsync(new CustomerListQuery(null, 1, 25, null, null, f.Jewellery.Id, null, null, null));
        var byRegion = await sut.ListAsync(new CustomerListQuery(null, 1, 25, null, null, null, "Gujarat", null, null));
        var byOwner = await sut.ListAsync(new CustomerListQuery(null, 1, 25, null, null, null, null, f.Owner.Id, null));
        var byTag = await sut.ListAsync(new CustomerListQuery(null, 1, 25, null, null, null, null, null, "VIP"));
        var bySearch = await sut.ListAsync(new CustomerListQuery("Kundan", 1, 25, null, null, null, null, null, null));

        byStatus.Items.Should().ContainSingle(i => i.Id == matching.Created!.Id);
        byServiceType.Items.Should().ContainSingle(i => i.Id == matching.Created!.Id);
        byCategory.Items.Should().ContainSingle(i => i.Id == matching.Created!.Id);
        byRegion.Items.Should().ContainSingle(i => i.Id == matching.Created!.Id);
        byOwner.Items.Should().ContainSingle(i => i.Id == matching.Created!.Id);
        byTag.Items.Should().ContainSingle(i => i.Id == matching.Created!.Id);
        bySearch.Items.Should().ContainSingle(i => i.Id == matching.Created!.Id);
        byServiceType.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_Paginates()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        for (var i = 0; i < 5; i++)
        {
            await sut.CreateAsync(ValidCifRequest(f, phone: $"90000000{i:D2}") with { Name = $"Customer {i}" }, Actor);
        }

        var page1 = await sut.ListAsync(new CustomerListQuery(null, 1, 2, null, null, null, null, null, null));
        var page2 = await sut.ListAsync(new CustomerListQuery(null, 2, 2, null, null, null, null, null, null));

        page1.Items.Should().HaveCount(2);
        page2.Items.Should().HaveCount(2);
        page1.TotalCount.Should().Be(5);
        page1.Items.Select(i => i.Id).Should().NotIntersectWith(page2.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var result = await sut.GetAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    // ---- Timeline: M6 additions (ShipmentRecorded, InvoiceCreated/InvoiceStatusChanged) -----

    [Fact]
    public async Task GetTimelineAsync_WithAShipment_IncludesAShipmentRecordedEvent()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor);

        var shipmentStatus = new ShipmentStatus { Id = Guid.NewGuid(), Code = "PACKED", Label = "Packed", IsActive = true, SortOrder = 1 };
        db.ShipmentStatuses.Add(shipmentStatus);
        var shipment = new Shipment
        {
            Id = Guid.NewGuid(), Reference = "SHP-2608-001", CustomerId = created.Created!.Id,
            ServiceTypeId = f.Cif.Id, StatusId = shipmentStatus.Id, Destination = "Mumbai", CreatedAt = DateTime.UtcNow
        };
        shipment.StatusHistory.Add(new ShipmentStatusHistory
        {
            Id = Guid.NewGuid(), ShipmentId = shipment.Id, StatusId = shipmentStatus.Id,
            ChangedByUserId = Actor, ChangedAt = DateTime.UtcNow, Note = "Shipment created."
        });
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();

        var timeline = await sut.GetTimelineAsync(created.Created.Id);

        var evt = timeline.Should().ContainSingle(e => e.Kind == TimelineEventKinds.ShipmentRecorded).Subject;
        evt.RefType.Should().Be("Shipment");
        evt.RefId.Should().Be(shipment.Id);
        evt.Body.Should().Contain("SHP-2608-001");
    }

    [Fact]
    public async Task GetTimelineAsync_WithAnIssuedInvoice_IncludesCreatedAndStatusChangedEvents_ButNotADuplicateForTheOpeningDraftRow()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor);

        var draft = new InvoiceStatus { Id = Guid.NewGuid(), Code = "DRAFT", Label = "Draft", IsActive = true, SortOrder = 1 };
        var issued = new InvoiceStatus { Id = Guid.NewGuid(), Code = "ISSUED", Label = "Issued", IsActive = true, SortOrder = 2 };
        db.InvoiceStatuses.AddRange(draft, issued);

        var invoiceCreatedAt = DateTime.UtcNow.AddMinutes(-10);
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(), CustomerId = created.Created!.Id, InvoiceNumber = "INV-2608-001",
            InvoiceDate = DateTime.UtcNow, Amount = 1000m, TaxAmount = 180m, Currency = "INR",
            StatusId = draft.Id, CreatedByUserId = Actor, CreatedAt = invoiceCreatedAt
        };
        invoice.StatusHistory.Add(new InvoiceStatusHistory
        {
            Id = Guid.NewGuid(), InvoiceId = invoice.Id, StatusId = draft.Id,
            ChangedByUserId = Actor, ChangedAt = invoiceCreatedAt, Note = "Invoice created."
        });
        invoice.StatusHistory.Add(new InvoiceStatusHistory
        {
            Id = Guid.NewGuid(), InvoiceId = invoice.Id, StatusId = issued.Id,
            ChangedByUserId = Actor, ChangedAt = DateTime.UtcNow, Note = null
        });
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var timeline = await sut.GetTimelineAsync(created.Created.Id);

        timeline.Should().ContainSingle(e => e.Kind == TimelineEventKinds.InvoiceCreated)
            .Which.Body.Should().Contain("INV-2608-001");
        // Exactly ONE status-changed event, not two: the opening Draft history row must not
        // ALSO surface as a status-changed event alongside InvoiceCreated (M6 GetTimelineAsync
        // doc comment) — that would double-report the same moment.
        timeline.Should().ContainSingle(e => e.Kind == TimelineEventKinds.InvoiceStatusChanged)
            .Which.Body.Should().Contain("Issued");
    }

    /// <summary>
    /// N-30: this timeline body is composed server-side and renders beside client-formatted
    /// amounts, so it must go through <c>MoneyFormatter</c> and carry Indian lakh grouping
    /// rather than a plain <c>0.00</c> format. Per D-64's rule, the assertion terminates in a
    /// LITERAL string, not in a value recomputed through the formatter under test.
    /// </summary>
    [Fact]
    public async Task GetTimelineAsync_InvoiceCreatedBody_UsesIndianLakhGroupedAmount_NotPlainDecimal()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidCifRequest(f), Actor);

        var draft = new InvoiceStatus { Id = Guid.NewGuid(), Code = "DRAFT", Label = "Draft", IsActive = true, SortOrder = 1 };
        db.InvoiceStatuses.Add(draft);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(), CustomerId = created.Created!.Id, InvoiceNumber = "INV-2608-002",
            InvoiceDate = DateTime.UtcNow, Amount = 145000m, TaxAmount = 2500m, Currency = "INR",
            StatusId = draft.Id, CreatedByUserId = Actor, CreatedAt = DateTime.UtcNow
        };
        invoice.StatusHistory.Add(new InvoiceStatusHistory
        {
            Id = Guid.NewGuid(), InvoiceId = invoice.Id, StatusId = draft.Id,
            ChangedByUserId = Actor, ChangedAt = DateTime.UtcNow, Note = "Invoice created."
        });
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var timeline = await sut.GetTimelineAsync(created.Created.Id);

        timeline.Should().ContainSingle(e => e.Kind == TimelineEventKinds.InvoiceCreated)
            .Which.Body.Should().Be("Invoice INV-2608-002 created for INR 1,47,500.00.");
    }
}
