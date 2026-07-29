using System.Text;
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
/// Covers ACTION_PLAN E5-07 / FR-VEN-07 (the DR-4 schema gap). <see cref="IFileStorage"/> is
/// mocked here, same reasoning as <c>CatalogServiceTests</c> — the real local-disk
/// implementation has its own test suite; this is about the service's own orchestration
/// (validation, mapping, and deliberately NOT versioning — FSD Q5 scopes vendor documents to
/// "filed for reference only").
/// </summary>
public class VendorDocumentServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private const long MaxSize = 10 * 1024 * 1024;

    private static VendorDocumentService CreateSut(AppDbContext db, out Mock<IAuditLogger> auditMock, out Mock<IFileStorage> storageMock)
    {
        auditMock = new Mock<IAuditLogger>();
        storageMock = new Mock<IFileStorage>();
        storageMock.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, CancellationToken _) => path);
        var options = new VendorUploadOptions { MaxUploadSizeBytes = MaxSize };
        return new VendorDocumentService(db, auditMock.Object, storageMock.Object, options);
    }

    private sealed record Fixture(Vendor Vendor, DocumentType Licence, DocumentType QualityCert);

    private static Fixture SeedMasterData(AppDbContext db)
    {
        var status = new VendorStatus { Id = Guid.NewGuid(), Code = "ACTIVE", Label = "Active", IsActive = true, SortOrder = 1 };
        db.VendorStatuses.Add(status);

        var licence = new DocumentType { Id = Guid.NewGuid(), Code = "BUSINESS_LICENCE", Label = "Business Licence", IsActive = true, SortOrder = 1 };
        var qualityCert = new DocumentType { Id = Guid.NewGuid(), Code = "QUALITY_CERTIFICATE", Label = "Quality Certificate", IsActive = true, SortOrder = 2 };
        db.DocumentTypes.AddRange(licence, qualityCert);

        var vendor = new Vendor { Id = Guid.NewGuid(), Name = "Golden Dragon Manufacturing", StatusId = status.Id, CreatedAt = DateTime.UtcNow };
        db.Vendors.Add(vendor);

        db.Users.Add(new User { Id = Actor, Name = "Acting Staff", Email = $"actor-{Guid.NewGuid():N}@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });

        db.SaveChanges();
        return new Fixture(vendor, licence, qualityCert);
    }

    private static Stream ValidPdfStream(int extraBytes = 100)
    {
        var bytes = new byte[4 + extraBytes];
        "%PDF"u8.CopyTo(bytes);
        return new MemoryStream(bytes);
    }

    // ---- List --------------------------------------------------------------------------

    [Fact]
    public async Task ListAsync_UnknownVendor_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _, out _);

        var result = await sut.ListAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_KnownVendor_ReturnsEmptyList_WhenNoDocumentsYet()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);

        var result = await sut.ListAsync(f.Vendor.Id);

        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_ReturnsDocumentsNewestFirst()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);
        using (var pdf1 = ValidPdfStream())
        {
            await sut.UploadDocumentAsync(f.Vendor.Id, pdf1, "first.pdf", "application/pdf", pdf1.Length, f.Licence.Id, Actor);
        }
        using (var pdf2 = ValidPdfStream())
        {
            await sut.UploadDocumentAsync(f.Vendor.Id, pdf2, "second.pdf", "application/pdf", pdf2.Length, f.QualityCert.Id, Actor);
        }

        var result = await sut.ListAsync(f.Vendor.Id);

        result!.Items.Should().HaveCount(2);
        result.Items.Select(i => i.OriginalFilename).Should().Contain(["first.pdf", "second.pdf"]);
    }

    // ---- Upload --------------------------------------------------------------------------

    [Fact]
    public async Task UploadDocumentAsync_UnknownVendor_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);
        using var pdf = ValidPdfStream();

        var result = await sut.UploadDocumentAsync(Guid.NewGuid(), pdf, "doc.pdf", "application/pdf", pdf.Length, f.Licence.Id, Actor);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UploadDocumentAsync_UnknownDocType_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);
        using var pdf = ValidPdfStream();

        var act = async () => await sut.UploadDocumentAsync(f.Vendor.Id, pdf, "doc.pdf", "application/pdf", pdf.Length, Guid.NewGuid(), Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task UploadDocumentAsync_ValidPdf_StoresAndReturnsDto_NeverExposesFilePath()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit, out var storage);
        using var pdf = ValidPdfStream();

        var result = await sut.UploadDocumentAsync(f.Vendor.Id, pdf, "licence.pdf", "application/pdf", pdf.Length, f.Licence.Id, Actor);

        result.Should().NotBeNull();
        result!.OriginalFilename.Should().Be("licence.pdf");
        result.VendorId.Should().Be(f.Vendor.Id);
        result.DocType.Code.Should().Be("BUSINESS_LICENCE");
        result.UploadedByName.Should().Be("Acting Staff");
        storage.Verify(s => s.SaveAsync(It.Is<string>(p => p.Contains($"vendor-docs/{f.Vendor.Id}/")), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(a => a.LogAsync(Actor, "VendorDocumentUploaded", "VendorDocument", result.Id.ToString(), It.IsAny<object>(), default), Times.Once);
        // No IsLatest/VersionLabel on the DTO shape at all — FSD Q5 explicitly excludes version history.
        typeof(VendorDocumentDto).GetProperty("IsLatest").Should().BeNull();
    }

    [Fact]
    public async Task UploadDocumentAsync_NonPdfContentType_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a pdf"));

        var act = async () => await sut.UploadDocumentAsync(f.Vendor.Id, stream, "fake.pdf", "image/png", stream.Length, f.Licence.Id, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task UploadDocumentAsync_SpoofedContentTypeWrongMagicBytes_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("NOTAPDF!binary garbage"));

        var act = async () => await sut.UploadDocumentAsync(f.Vendor.Id, stream, "fake.pdf", "application/pdf", stream.Length, f.Licence.Id, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task UploadDocumentAsync_OversizeFile_ThrowsValidationException()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);
        using var pdf = ValidPdfStream();

        var act = async () => await sut.UploadDocumentAsync(f.Vendor.Id, pdf, "big.pdf", "application/pdf", MaxSize + 1, f.Licence.Id, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    // ---- Download --------------------------------------------------------------------------

    [Fact]
    public async Task DownloadDocumentAsync_UnknownId_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _, out _);

        var result = await sut.DownloadDocumentAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task DownloadDocumentAsync_KnownId_ReturnsStreamAndFilename()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var contentStream = new MemoryStream(Encoding.UTF8.GetBytes("pdf-bytes"));
        var sut = CreateSut(db, out _, out var storage);
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(contentStream);
        using var pdf = ValidPdfStream();
        var doc = await sut.UploadDocumentAsync(f.Vendor.Id, pdf, "licence.pdf", "application/pdf", pdf.Length, f.Licence.Id, Actor);

        var download = await sut.DownloadDocumentAsync(doc!.Id);

        download.Should().NotBeNull();
        download!.OriginalFilename.Should().Be("licence.pdf");
        download.ContentType.Should().Be("application/pdf");
    }

    // ---- Delete --------------------------------------------------------------------------

    [Fact]
    public async Task DeleteDocumentAsync_UnknownId_ReturnsFalse()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _, out _);

        var result = await sut.DeleteDocumentAsync(Guid.NewGuid(), Actor);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteDocumentAsync_KnownId_RemovesRowAndCallsFileStorageDelete_AndAudits()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit, out var storage);
        using var pdf = ValidPdfStream();
        var doc = await sut.UploadDocumentAsync(f.Vendor.Id, pdf, "licence.pdf", "application/pdf", pdf.Length, f.Licence.Id, Actor);

        var deleted = await sut.DeleteDocumentAsync(doc!.Id, Actor);

        deleted.Should().BeTrue();
        (await db.VendorDocuments.FindAsync(doc.Id)).Should().BeNull();
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(a => a.LogAsync(Actor, "VendorDocumentDeleted", "VendorDocument", doc.Id.ToString(), It.IsAny<object>(), default), Times.Once);
    }
}
