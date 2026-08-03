using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SourcingOps.Application.Common;
using SourcingOps.Application.Dispatching;
using SourcingOps.Application.Interfaces;
using SourcingOps.Application.Tests.TestSupport;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Application.Tests.Dispatching;

/// <summary>
/// Covers ACTION_PLAN E9-02/E9-06/E9-07 (compose, create, sent-to history). E9-01's actual
/// deep-link building is unit-tested in isolation in <see cref="WhatsAppDeepLinkSenderTests"/>;
/// <see cref="IDispatchMessageSender"/> is mocked here so this suite is purely about
/// <see cref="DispatchService"/>'s own orchestration (lookup/validation/mapping/audit) — same
/// separation <c>CatalogServiceTests</c> draws for <see cref="IFileStorage"/>.
/// </summary>
public class DispatchServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private static DispatchService CreateSut(
        AppDbContext db, out Mock<IAuditLogger> auditMock, out Mock<IDispatchMessageSender> senderMock, DispatchOptions? options = null)
    {
        auditMock = new Mock<IAuditLogger>();
        senderMock = new Mock<IDispatchMessageSender>();
        senderMock.Setup(s => s.Prepare(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string phone, string message) => new DispatchSendPreparation($"https://wa.me/{phone.TrimStart('+')}?text=stub"));
        return new DispatchService(db, auditMock.Object, senderMock.Object, options ?? new DispatchOptions());
    }

    private sealed record Fixture(Customer Customer, CatalogDocument Document, CatalogSection Section, User Staff);

    private static Fixture SeedData(AppDbContext db)
    {
        var vendorStatus = new VendorStatus { Id = Guid.NewGuid(), Code = "ACTIVE", Label = "Active", IsActive = true, SortOrder = 1 };
        db.VendorStatuses.Add(vendorStatus);

        var category = new Category { Id = Guid.NewGuid(), Name = "Jewellery", IsActive = true, SortOrder = 1 };
        db.Categories.Add(category);

        var vendor = new Vendor { Id = Guid.NewGuid(), Name = "Golden Dragon Manufacturing", StatusId = vendorStatus.Id, CreatedAt = DateTime.UtcNow };
        db.Vendors.Add(vendor);

        var serviceType = new ServiceType { Id = Guid.NewGuid(), Code = "CIF", Label = "CIF", IsActive = true, SortOrder = 1 };
        db.ServiceTypes.Add(serviceType);

        var leadStatus = new LeadStatus { Id = Guid.NewGuid(), Code = "NEW", Label = "New", IsActive = true, SortOrder = 1 };
        db.LeadStatuses.Add(leadStatus);

        var staff = new User { Id = Actor, Name = "Acting Staff", Email = $"actor-{Guid.NewGuid():N}@example.com", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Users.Add(staff);

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Name = "Kundan Traders",
            Phone = "+919825041122",
            ServiceTypeId = serviceType.Id,
            ServiceType = serviceType,
            StatusId = leadStatus.Id,
            Status = leadStatus,
            CreatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);

        var section = new CatalogSection
        {
            Id = Guid.NewGuid(),
            VendorId = vendor.Id,
            Vendor = vendor,
            Title = "Spring 2026 Collection",
            CategoryId = category.Id,
            Category = category,
            CreatedAt = DateTime.UtcNow
        };
        db.CatalogSections.Add(section);

        var document = new CatalogDocument
        {
            Id = Guid.NewGuid(),
            CatalogSectionId = section.Id,
            CatalogSection = section,
            FilePath = "catalog-docs/x/y.pdf",
            OriginalFilename = "spring-2026.pdf",
            SizeBytes = 1024,
            IsLatest = true,
            UploadedByUserId = staff.Id,
            UploadedBy = staff,
            UploadedAt = DateTime.UtcNow
        };
        db.CatalogDocuments.Add(document);

        db.SaveChanges();
        return new Fixture(customer, document, section, staff);
    }

    // ---- Compose (E9-01, E9-06) --------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_ValidCustomerAndDocument_RendersTemplate_AndBuildsDeepLink()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedData(db);
        var sut = CreateSut(db, out _, out var sender);

        var result = await sut.ComposeAsync(f.Customer.Id, f.Document.Id);

        result.Should().NotBeNull();
        result!.Message.Should().Contain("Kundan Traders").And.Contain("Spring 2026 Collection");
        result.DeepLinkUrl.Should().Be("https://wa.me/919825041122?text=stub");
        sender.Verify(s => s.Prepare(f.Customer.Phone, result.Message), Times.Once);
    }

    [Fact]
    public async Task ComposeAsync_CustomTemplate_SubstitutesBothPlaceholders()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedData(db);
        var options = new DispatchOptions { MessageTemplate = "Dear {CustomerName}, see {CatalogName} attached." };
        var sut = CreateSut(db, out _, out _, options);

        var result = await sut.ComposeAsync(f.Customer.Id, f.Document.Id);

        result!.Message.Should().Be("Dear Kundan Traders, see Spring 2026 Collection attached.");
    }

    [Fact]
    public async Task ComposeAsync_UnknownCustomer_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedData(db);
        var sut = CreateSut(db, out _, out _);

        var result = await sut.ComposeAsync(Guid.NewGuid(), f.Document.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ComposeAsync_UnknownCatalogDocument_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedData(db);
        var sut = CreateSut(db, out _, out _);

        var result = await sut.ComposeAsync(f.Customer.Id, Guid.NewGuid());

        result.Should().BeNull();
    }

    // ---- Create (E9-02) -----------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_Valid_RecordsDispatch_WithActorAsStaffUser_AndAudits()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedData(db);
        var sut = CreateSut(db, out var audit, out _);
        var request = new CreateDispatchLogRequest(f.Customer.Id, f.Document.Id, null, "Hi Kundan Traders, here is our catalog.");

        var result = await sut.CreateAsync(request, Actor);

        result.CustomerId.Should().Be(f.Customer.Id);
        result.CustomerName.Should().Be("Kundan Traders");
        result.CatalogDocumentId.Should().Be(f.Document.Id);
        result.CatalogName.Should().Be("Spring 2026 Collection");
        result.StaffUserId.Should().Be(Actor); // never taken from the request body — CreateDispatchLogRequest has no staff field at all
        result.StaffUserName.Should().Be("Acting Staff");
        result.Message.Should().Be("Hi Kundan Traders, here is our catalog.");

        var stored = await db.Dispatches.SingleAsync();
        stored.StaffUserId.Should().Be(Actor);
        stored.CustomerId.Should().Be(f.Customer.Id);
        stored.CatalogDocumentId.Should().Be(f.Document.Id);

        audit.Verify(a => a.LogAsync(Actor, "DispatchLogged", "Dispatch", result.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_BlankMessage_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedData(db);
        var sut = CreateSut(db, out _, out _);
        var request = new CreateDispatchLogRequest(f.Customer.Id, f.Document.Id, null, "   ");

        var act = async () => await sut.CreateAsync(request, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task CreateAsync_UnknownCustomerId_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedData(db);
        var sut = CreateSut(db, out _, out _);
        var request = new CreateDispatchLogRequest(Guid.NewGuid(), f.Document.Id, null, "Hi there");

        var act = async () => await sut.CreateAsync(request, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task CreateAsync_UnknownCatalogDocumentId_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedData(db);
        var sut = CreateSut(db, out _, out _);
        var request = new CreateDispatchLogRequest(f.Customer.Id, Guid.NewGuid(), null, "Hi there");

        var act = async () => await sut.CreateAsync(request, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    // ---- M6/E8-06: invoice dispatch --------------------------------------------------------

    private static Invoice SeedInvoice(AppDbContext db, Customer customer)
    {
        var draft = new InvoiceStatus { Id = Guid.NewGuid(), Code = "DRAFT", Label = "Draft", IsActive = true, SortOrder = 1 };
        db.InvoiceStatuses.Add(draft);
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(), CustomerId = customer.Id, InvoiceNumber = "INV-2608-001",
            InvoiceDate = DateTime.UtcNow, Amount = 1000m, TaxAmount = 180m, Currency = "INR",
            StatusId = draft.Id, CreatedByUserId = Actor, CreatedAt = DateTime.UtcNow
        };
        db.Invoices.Add(invoice);
        db.SaveChanges();
        return invoice;
    }

    [Fact]
    public async Task CreateAsync_InvoiceTarget_RecordsDispatch_AgainstTheInvoice()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedData(db);
        var invoice = SeedInvoice(db, f.Customer);
        var sut = CreateSut(db, out _, out _);
        var request = new CreateDispatchLogRequest(f.Customer.Id, null, invoice.Id, "Sharing your invoice.");

        var result = await sut.CreateAsync(request, Actor);

        result.CatalogDocumentId.Should().BeNull();
        result.CatalogName.Should().BeNull();
        result.InvoiceId.Should().Be(invoice.Id);
        result.InvoiceNumber.Should().Be("INV-2608-001");

        var stored = await db.Dispatches.SingleAsync();
        stored.CatalogDocumentId.Should().BeNull();
        stored.InvoiceId.Should().Be(invoice.Id);
    }

    [Fact]
    public async Task CreateAsync_NeitherCatalogNorInvoiceSupplied_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedData(db);
        var sut = CreateSut(db, out _, out _);
        var request = new CreateDispatchLogRequest(f.Customer.Id, null, null, "Hi there");

        var act = async () => await sut.CreateAsync(request, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("catalogDocumentId");
    }

    [Fact]
    public async Task CreateAsync_BothCatalogAndInvoiceSupplied_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedData(db);
        var invoice = SeedInvoice(db, f.Customer);
        var sut = CreateSut(db, out _, out _);
        var request = new CreateDispatchLogRequest(f.Customer.Id, f.Document.Id, invoice.Id, "Hi there");

        var act = async () => await sut.CreateAsync(request, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("catalogDocumentId");
    }

    [Fact]
    public async Task CreateAsync_UnknownInvoiceId_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedData(db);
        var sut = CreateSut(db, out _, out _);
        var request = new CreateDispatchLogRequest(f.Customer.Id, null, Guid.NewGuid(), "Hi there");

        var act = async () => await sut.CreateAsync(request, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("invoiceId");
    }

    // ---- Sent-to history (E9-07) ---------------------------------------------------------

    [Fact]
    public async Task GetDocumentHistoryAsync_UnknownDocument_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _, out _);

        var result = await sut.GetDocumentHistoryAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDocumentHistoryAsync_NoDispatchesYet_ReturnsEmptyList()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedData(db);
        var sut = CreateSut(db, out _, out _);

        var result = await sut.GetDocumentHistoryAsync(f.Document.Id);

        result.Should().NotBeNull();
        result!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDocumentHistoryAsync_ReturnsNewestFirst_WithCustomerAndStaffNamesResolved()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedData(db);
        var sut = CreateSut(db, out _, out _);
        var first = await sut.CreateAsync(new CreateDispatchLogRequest(f.Customer.Id, f.Document.Id, null, "First send"), Actor);
        await Task.Delay(10); // ensure a distinct, later SentAt for the ordering assertion
        var second = await sut.CreateAsync(new CreateDispatchLogRequest(f.Customer.Id, f.Document.Id, null, "Second send"), Actor);

        var result = await sut.GetDocumentHistoryAsync(f.Document.Id);

        result.Should().HaveCount(2);
        result![0].DispatchId.Should().Be(second.Id);
        result[1].DispatchId.Should().Be(first.Id);
        result[0].CustomerName.Should().Be("Kundan Traders");
        result[0].StaffUserName.Should().Be("Acting Staff");
    }
}
