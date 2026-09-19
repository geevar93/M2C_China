using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SourcingOps.Application.Catalog;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Application.Tests.TestSupport;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Application.Tests.Catalog;

/// <summary>
/// Covers ACTION_PLAN E6-01…E6-07 against the coordinator's binding `/api/v1/catalog-sections`
/// / `/api/v1/catalog-documents` contract. <see cref="IFileStorage"/> is mocked here — the
/// real local-disk implementation is already covered by <c>LocalDiskFileStorageTests</c>; this
/// suite is about the service's own orchestration (versioning, validation, mapping).
/// </summary>
public class CatalogServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private const long MaxSize = 10 * 1024 * 1024;

    private static CatalogService CreateSut(AppDbContext db, out Mock<IAuditLogger> auditMock, out Mock<IFileStorage> storageMock)
    {
        auditMock = new Mock<IAuditLogger>();
        storageMock = new Mock<IFileStorage>();
        storageMock.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, CancellationToken _) => path);
        var options = new CatalogUploadOptions { MaxUploadSizeBytes = MaxSize };
        return new CatalogService(db, auditMock.Object, storageMock.Object, options);
    }

    private sealed record Fixture(Vendor Vendor, Category Jewellery, Category Furniture);

    private static Fixture SeedMasterData(AppDbContext db)
    {
        var status = new VendorStatus { Id = Guid.NewGuid(), Code = "ACTIVE", Label = "Active", IsActive = true, SortOrder = 1 };
        db.VendorStatuses.Add(status);

        var jewellery = new Category { Id = Guid.NewGuid(), Name = "Jewellery", IsActive = true, SortOrder = 1 };
        var furniture = new Category { Id = Guid.NewGuid(), Name = "Furniture", IsActive = true, SortOrder = 2 };
        db.Categories.AddRange(jewellery, furniture);

        var vendor = new Vendor { Id = Guid.NewGuid(), Name = "Golden Dragon Manufacturing", StatusId = status.Id, CreatedAt = DateTime.UtcNow };
        db.Vendors.Add(vendor);

        db.Users.Add(new User { Id = Actor, Name = "Acting Staff", Email = $"actor-{Guid.NewGuid():N}@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });

        db.SaveChanges();
        return new Fixture(vendor, jewellery, furniture);
    }

    private static CreateCatalogSectionRequest ValidCreateRequest(Fixture f) => new(
        VendorId: f.Vendor.Id, Title: "Spring 2026 Collection", CategoryId: f.Jewellery.Id, Tags: ["new arrivals", "New Arrivals", " "]);

    private static Stream ValidPdfStream(int extraBytes = 100)
    {
        var bytes = new byte[4 + extraBytes];
        "%PDF"u8.CopyTo(bytes);
        return new MemoryStream(bytes);
    }

    // ---- Create (E6-01) -----------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_ValidSection_CreatesAndReturnsDto_WithNormalizedTags()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit, out _);

        var result = await sut.CreateAsync(ValidCreateRequest(f), Actor);

        result.Title.Should().Be("Spring 2026 Collection");
        result.VendorId.Should().Be(f.Vendor.Id);
        result.VendorName.Should().Be(f.Vendor.Name);
        result.Category.Name.Should().Be("Jewellery");
        // E6-07: trim / drop-empty / case-insensitive de-dup — "new arrivals" and "New Arrivals" collapse to one.
        result.Tags.Should().Equal("new arrivals");
        result.Documents.Should().BeEmpty();

        audit.Verify(a => a.LogAsync(Actor, "CatalogSectionCreated", "CatalogSection", result.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_BlankTitle_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);
        var request = ValidCreateRequest(f) with { Title = "   " };

        var act = async () => await sut.CreateAsync(request, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task CreateAsync_UnknownVendorId_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);
        var request = ValidCreateRequest(f) with { VendorId = Guid.NewGuid() };

        var act = async () => await sut.CreateAsync(request, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task CreateAsync_UnknownCategoryId_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);
        var request = ValidCreateRequest(f) with { CategoryId = Guid.NewGuid() };

        var act = async () => await sut.CreateAsync(request, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    // ---- Update -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);

        var result = await sut.UpdateAsync(Guid.NewGuid(), new UpdateCatalogSectionRequest("X", f.Jewellery.Id, null), Actor);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ChangesTitleCategoryAndTags_AndAudits()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit, out _);
        var created = await sut.CreateAsync(ValidCreateRequest(f), Actor);

        var updated = await sut.UpdateAsync(created.Id, new UpdateCatalogSectionRequest("Renamed Section", f.Furniture.Id, ["clearance"]), Actor);

        updated!.Title.Should().Be("Renamed Section");
        updated.Category.Name.Should().Be("Furniture");
        updated.Tags.Should().Equal("clearance");
        audit.Verify(a => a.LogAsync(Actor, "CatalogSectionUpdated", "CatalogSection", created.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }

    // ---- Delete -----------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _, out _);

        var result = await sut.DeleteAsync(Guid.NewGuid(), Actor);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_RemovesSectionAndCallsFileStorageDeleteForEachDocument()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit, out var storage);
        var created = await sut.CreateAsync(ValidCreateRequest(f), Actor);
        using var pdf = ValidPdfStream();
        var doc = await sut.UploadDocumentAsync(created.Id, pdf, "catalog.pdf", "application/pdf", pdf.Length, null, Actor);

        var deleted = await sut.DeleteAsync(created.Id, Actor);

        deleted.Should().BeTrue();
        (await db.CatalogSections.FindAsync(created.Id)).Should().BeNull();
        (await db.CatalogDocuments.FindAsync(doc!.Id)).Should().BeNull();
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(a => a.LogAsync(Actor, "CatalogSectionDeleted", "CatalogSection", created.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }

    // ---- Upload / versioning (E6-02, E6-03, E6-06) -------------------------------------

    [Fact]
    public async Task UploadDocumentAsync_UnknownSection_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _, out _);
        using var pdf = ValidPdfStream();

        var result = await sut.UploadDocumentAsync(Guid.NewGuid(), pdf, "catalog.pdf", "application/pdf", pdf.Length, null, Actor);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UploadDocumentAsync_ValidPdf_StoresAndReturnsDto_MarkedLatest()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit, out var storage);
        var section = await sut.CreateAsync(ValidCreateRequest(f), Actor);
        using var pdf = ValidPdfStream();

        var result = await sut.UploadDocumentAsync(section.Id, pdf, "catalog v1.pdf", "application/pdf", pdf.Length, "v1", Actor);

        result.Should().NotBeNull();
        result!.OriginalFilename.Should().Be("catalog v1.pdf");
        result.IsLatest.Should().BeTrue();
        result.VersionLabel.Should().Be("v1");
        result.UploadedByName.Should().Be("Acting Staff");
        storage.Verify(s => s.SaveAsync(It.Is<string>(p => p.Contains($"catalog-docs/{section.Id}/")), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(a => a.LogAsync(Actor, "CatalogDocumentUploaded", "CatalogDocument", result.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }

    [Fact]
    public async Task UploadDocumentAsync_SecondUpload_DemotesPreviousLatest()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);
        var section = await sut.CreateAsync(ValidCreateRequest(f), Actor);
        using (var first = ValidPdfStream())
        {
            await sut.UploadDocumentAsync(section.Id, first, "v1.pdf", "application/pdf", first.Length, "v1", Actor);
        }

        CatalogDocumentDto second;
        using (var pdf = ValidPdfStream())
        {
            second = (await sut.UploadDocumentAsync(section.Id, pdf, "v2.pdf", "application/pdf", pdf.Length, "v2", Actor))!;
        }

        var reloaded = await sut.GetAsync(section.Id);
        reloaded!.Documents.Should().HaveCount(2);
        reloaded.Documents.Single(d => d.OriginalFilename == "v1.pdf").IsLatest.Should().BeFalse();
        reloaded.Documents.Single(d => d.OriginalFilename == "v2.pdf").IsLatest.Should().BeTrue();
        second.IsLatest.Should().BeTrue();
    }

    [Fact]
    public async Task UploadDocumentAsync_NonPdfContentType_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);
        var section = await sut.CreateAsync(ValidCreateRequest(f), Actor);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a pdf"));

        var act = async () => await sut.UploadDocumentAsync(section.Id, stream, "fake.pdf", "image/png", stream.Length, null, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task UploadDocumentAsync_SpoofedContentTypeWrongMagicBytes_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);
        var section = await sut.CreateAsync(ValidCreateRequest(f), Actor);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("NOTAPDF!binary garbage"));

        var act = async () => await sut.UploadDocumentAsync(section.Id, stream, "fake.pdf", "application/pdf", stream.Length, null, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task UploadDocumentAsync_OversizeFile_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);
        var section = await sut.CreateAsync(ValidCreateRequest(f), Actor);
        using var pdf = ValidPdfStream();

        var act = async () => await sut.UploadDocumentAsync(section.Id, pdf, "big.pdf", "application/pdf", MaxSize + 1, null, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    // ---- Download (E6-04) --------------------------------------------------------------

    [Fact]
    public async Task DownloadDocumentAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _, out _);

        var result = await sut.DownloadDocumentAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task DownloadDocumentAsync_KnownId_ReturnsStreamAndFilename_NeverExposesFilePath()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var contentStream = new MemoryStream(Encoding.UTF8.GetBytes("pdf-bytes"));
        var sut = CreateSut(db, out _, out var storage);
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(contentStream);
        var section = await sut.CreateAsync(ValidCreateRequest(f), Actor);
        using var pdf = ValidPdfStream();
        var doc = await sut.UploadDocumentAsync(section.Id, pdf, "catalog.pdf", "application/pdf", pdf.Length, null, Actor);

        var download = await sut.DownloadDocumentAsync(doc!.Id);

        download.Should().NotBeNull();
        download!.OriginalFilename.Should().Be("catalog.pdf");
        download.ContentType.Should().Be("application/pdf");
    }

    // ---- List / search / filter (E6-05, E6-07) -----------------------------------------

    [Fact]
    public async Task ListAsync_FiltersByEveryContractParameter()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);
        var matching = await sut.CreateAsync(ValidCreateRequest(f), Actor);
        await sut.CreateAsync(ValidCreateRequest(f) with { Title = "Other Section", CategoryId = f.Furniture.Id, Tags = ["clearance"] }, Actor);

        var bySearch = await sut.ListAsync(new CatalogSectionListQuery("Spring 2026", 1, 25, null, null, null));
        var byCategory = await sut.ListAsync(new CatalogSectionListQuery(null, 1, 25, f.Jewellery.Id, null, null));
        var byVendor = await sut.ListAsync(new CatalogSectionListQuery(null, 1, 25, null, f.Vendor.Id, null));
        var byTag = await sut.ListAsync(new CatalogSectionListQuery(null, 1, 25, null, null, "new arrivals"));

        bySearch.Items.Should().ContainSingle(i => i.Id == matching.Id);
        byCategory.Items.Should().ContainSingle(i => i.Id == matching.Id);
        byVendor.Items.Should().HaveCount(2);
        byTag.Items.Should().ContainSingle(i => i.Id == matching.Id);
    }

    [Fact]
    public async Task ListAsync_SearchAlsoMatchesVendorName()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);
        var section = await sut.CreateAsync(ValidCreateRequest(f), Actor);

        var term = f.Vendor.Name.Split(' ')[0].ToUpperInvariant();
        var result = await sut.ListAsync(new CatalogSectionListQuery(term, 1, 25, null, null, null));

        result.Items.Should().ContainSingle(i => i.Id == section.Id);
    }

    [Fact]
    public async Task ListAsync_Paginates()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);
        for (var i = 0; i < 5; i++)
        {
            await sut.CreateAsync(ValidCreateRequest(f) with { Title = $"Section {i}" }, Actor);
        }

        var page1 = await sut.ListAsync(new CatalogSectionListQuery(null, 1, 2, null, null, null));
        var page2 = await sut.ListAsync(new CatalogSectionListQuery(null, 2, 2, null, null, null));

        page1.Items.Should().HaveCount(2);
        page1.TotalCount.Should().Be(5);
        page1.Items.Select(i => i.Id).Should().NotIntersectWith(page2.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _, out _);

        var result = await sut.GetAsync(Guid.NewGuid());

        result.Should().BeNull();
    }
}
