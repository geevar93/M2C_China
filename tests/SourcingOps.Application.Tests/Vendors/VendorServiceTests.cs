using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Application.Tests.TestSupport;
using SourcingOps.Application.Vendors;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Application.Tests.Vendors;

/// <summary>
/// Covers ACTION_PLAN E5-01…E5-06 against the coordinator's binding `/api/v1/vendors`
/// contract. Uses the EF Core InMemory provider (see <see cref="TestDbContextFactory"/>) —
/// <see cref="VendorService"/> deliberately materializes list pages before mapping (same
/// defensive pattern as <c>CustomerService</c>/<c>MasterDataService</c>).
/// </summary>
public class VendorServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private static VendorService CreateSut(AppDbContext db, out Mock<IAuditLogger> auditMock)
    {
        auditMock = new Mock<IAuditLogger>();
        return new VendorService(db, auditMock.Object);
    }

    private sealed record Fixture(VendorStatus Active, VendorStatus OnHold, Category Jewellery, Category Furniture);

    private static Fixture SeedMasterData(AppDbContext db)
    {
        var active = new VendorStatus { Id = Guid.NewGuid(), Code = "ACTIVE", Label = "Active", IsActive = true, SortOrder = 1 };
        var onHold = new VendorStatus { Id = Guid.NewGuid(), Code = "ON-HOLD", Label = "On Hold", IsActive = true, SortOrder = 2 };
        db.VendorStatuses.AddRange(active, onHold);

        var jewellery = new Category { Id = Guid.NewGuid(), Name = "Jewellery", IsActive = true, SortOrder = 1 };
        var furniture = new Category { Id = Guid.NewGuid(), Name = "Furniture", IsActive = true, SortOrder = 2 };
        db.Categories.AddRange(jewellery, furniture);

        db.Users.Add(new User { Id = Actor, Name = "Acting Staff", Email = $"actor-{Guid.NewGuid():N}@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });

        db.SaveChanges();
        return new Fixture(active, onHold, jewellery, furniture);
    }

    private static CreateVendorRequest ValidRequest(Fixture f) => new(
        Name: "Golden Dragon Manufacturing",
        ContactPerson: "Li Wei",
        Phone: "+86-138-0000-0000",
        Email: "li.wei@example.com",
        Region: "Yiwu, Zhejiang",
        StatusId: f.Active.Id,
        CategoryIds: [f.Jewellery.Id],
        Moq: "500 units",
        LeadTime: "15-20 days",
        PaymentTerms: "30% deposit, 70% before shipment",
        ReliabilityRating: 4.5m,
        Notes: "Reliable long-term partner.");

    // ---- Create (E5-01…E5-03, E5-06) --------------------------------------------------

    [Fact]
    public async Task CreateAsync_ValidVendor_CreatesAndReturnsDetail_WithEmbeddedStatusAndCategories()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit);

        var result = await sut.CreateAsync(ValidRequest(f), Actor);

        result.Name.Should().Be("Golden Dragon Manufacturing");
        result.Region.Should().Be("Yiwu, Zhejiang");
        result.Status.Code.Should().Be("ACTIVE");
        result.Status.Label.Should().Be("Active");
        result.Categories.Should().ContainSingle(c => c.Name == "Jewellery");
        result.PaymentTerms.Should().Be("30% deposit, 70% before shipment");
        result.ReliabilityRating.Should().Be(4.5m);
        result.CatalogCount.Should().Be(0);
        result.CatalogSections.Should().BeEmpty();

        audit.Verify(a => a.LogAsync(Actor, "VendorCreated", "Vendor", result.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_BlankName_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var request = ValidRequest(f) with { Name = "   " };

        var act = async () => await sut.CreateAsync(request, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task CreateAsync_UnknownStatusId_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var request = ValidRequest(f) with { StatusId = Guid.NewGuid() };

        var act = async () => await sut.CreateAsync(request, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task CreateAsync_UnknownCategoryId_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var request = ValidRequest(f) with { CategoryIds = [Guid.NewGuid()] };

        var act = async () => await sut.CreateAsync(request, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task CreateAsync_NoCategoryIds_Succeeds_WithEmptyCategories()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var request = ValidRequest(f) with { CategoryIds = null };

        var result = await sut.CreateAsync(request, Actor);

        result.Categories.Should().BeEmpty();
    }

    // ---- Update -------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var request = new UpdateVendorRequest("X", null, null, null, null, f.Active.Id, null, null, null, null, null, null);

        var result = await sut.UpdateAsync(Guid.NewGuid(), request, Actor);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ChangesStatusAndCategories_AndAudits()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit);
        var created = await sut.CreateAsync(ValidRequest(f), Actor);

        var updateRequest = new UpdateVendorRequest(
            created.Name, created.ContactPerson, created.Phone, created.Email, created.Region,
            f.OnHold.Id, [f.Furniture.Id], created.Moq, created.LeadTime, created.PaymentTerms,
            created.ReliabilityRating, created.Notes);

        var updated = await sut.UpdateAsync(created.Id, updateRequest, Actor);

        updated!.Status.Code.Should().Be("ON-HOLD");
        updated.Categories.Should().ContainSingle(c => c.Name == "Furniture");
        audit.Verify(a => a.LogAsync(Actor, "VendorUpdated", "Vendor", created.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_BlankName_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var created = await sut.CreateAsync(ValidRequest(f), Actor);
        var updateRequest = new UpdateVendorRequest(
            "  ", created.ContactPerson, created.Phone, created.Email, created.Region,
            f.Active.Id, created.Categories.Select(c => c.Id).ToList(), created.Moq, created.LeadTime,
            created.PaymentTerms, created.ReliabilityRating, created.Notes);

        var act = async () => await sut.UpdateAsync(created.Id, updateRequest, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    // ---- List / search / filter (E5-04) ----------------------------------------------

    [Fact]
    public async Task ListAsync_FiltersByEveryContractParameter()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var matching = await sut.CreateAsync(ValidRequest(f), Actor);
        await sut.CreateAsync(ValidRequest(f) with
        {
            Name = "Other Supplier", StatusId = f.OnHold.Id, CategoryIds = [f.Furniture.Id], Region = "Shenzhen"
        }, Actor);

        var bySearch = await sut.ListAsync(new VendorListQuery("Golden Dragon", 1, 25, null, null, null));
        var byCategory = await sut.ListAsync(new VendorListQuery(null, 1, 25, f.Jewellery.Id, null, null));
        var byRegion = await sut.ListAsync(new VendorListQuery(null, 1, 25, null, "Yiwu, Zhejiang", null));
        var byStatus = await sut.ListAsync(new VendorListQuery(null, 1, 25, null, null, f.Active.Id));

        bySearch.Items.Should().ContainSingle(i => i.Id == matching.Id);
        byCategory.Items.Should().ContainSingle(i => i.Id == matching.Id);
        byRegion.Items.Should().ContainSingle(i => i.Id == matching.Id);
        byStatus.Items.Should().ContainSingle(i => i.Id == matching.Id);
    }

    [Fact]
    public async Task ListAsync_Paginates()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        for (var i = 0; i < 5; i++)
        {
            await sut.CreateAsync(ValidRequest(f) with { Name = $"Vendor {i}" }, Actor);
        }

        var page1 = await sut.ListAsync(new VendorListQuery(null, 1, 2, null, null, null));
        var page2 = await sut.ListAsync(new VendorListQuery(null, 2, 2, null, null, null));

        page1.Items.Should().HaveCount(2);
        page1.TotalCount.Should().Be(5);
        page1.Items.Select(i => i.Id).Should().NotIntersectWith(page2.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task ListAsync_ShowsStatusInEveryListing()
    {
        // E5-03: status is shown in every listing, not just detail.
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        await sut.CreateAsync(ValidRequest(f), Actor);

        var result = await sut.ListAsync(new VendorListQuery(null, 1, 25, null, null, null));

        result.Items.Should().OnlyContain(i => i.Status != null && i.Status.Code == "ACTIVE");
    }

    // ---- Get -------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _);

        var result = await sut.GetAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_EmbedsCatalogSectionsAndDocuments()
    {
        // E5-05: vendor detail response includes its catalog sections and their documents.
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _);
        var vendor = await sut.CreateAsync(ValidRequest(f), Actor);

        var section = new CatalogSection
        {
            Id = Guid.NewGuid(), VendorId = vendor.Id, Title = "Spring 2026 Collection",
            CategoryId = f.Jewellery.Id, Tags = ["new arrivals"], CreatedAt = DateTime.UtcNow
        };
        db.CatalogSections.Add(section);
        db.CatalogDocuments.Add(new CatalogDocument
        {
            Id = Guid.NewGuid(), CatalogSectionId = section.Id, FilePath = "catalog-docs/x/doc.pdf",
            OriginalFilename = "catalog.pdf", SizeBytes = 1024, IsLatest = true, UploadedByUserId = Actor, UploadedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var detail = await sut.GetAsync(vendor.Id);

        detail!.CatalogCount.Should().Be(1);
        detail.CatalogSections.Should().ContainSingle(s => s.Title == "Spring 2026 Collection");
        detail.CatalogSections.Single().Documents.Should().ContainSingle(d => d.OriginalFilename == "catalog.pdf");
        detail.CatalogSections.Single().VendorName.Should().Be(vendor.Name);
    }
}
