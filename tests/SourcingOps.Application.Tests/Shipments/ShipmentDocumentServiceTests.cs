using FluentAssertions;
using Moq;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Application.Shipments;
using SourcingOps.Application.Tests.TestSupport;
using SourcingOps.Domain.Common;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Application.Tests.Shipments;

/// <summary>
/// Covers ACTION_PLAN E7-09 / FR-INV-08. <see cref="IFileStorage"/> is mocked, same reasoning
/// as <c>VendorDocumentServiceTests</c> — the real local-disk implementation has its own suite;
/// this is about the service's orchestration: upload validation, the D-f scope guard, the
/// TECH_SPEC §4.6 path, and never letting <c>FilePath</c> escape the service boundary.
/// </summary>
public class ShipmentDocumentServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private const long MaxSize = 10 * 1024 * 1024;

    private static ShipmentDocumentService CreateSut(AppDbContext db, out Mock<IAuditLogger> auditMock, out Mock<IFileStorage> storageMock)
    {
        auditMock = new Mock<IAuditLogger>();
        storageMock = new Mock<IFileStorage>();
        storageMock.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Stream _, CancellationToken _) => path);
        var options = new ShipmentUploadOptions { MaxUploadSizeBytes = MaxSize };
        return new ShipmentDocumentService(db, auditMock.Object, storageMock.Object, options);
    }

    private sealed record Fixture(Shipment Shipment, DocumentType PackingList, DocumentType BusinessLicence);

    private static Fixture SeedMasterData(AppDbContext db)
    {
        var cif = new ServiceType { Id = Guid.NewGuid(), Code = "CIF", Label = "CIF", IsActive = true, SortOrder = 1 };
        var packed = new ShipmentStatus { Id = Guid.NewGuid(), Code = "PACKED", Label = "Packed", IsActive = true, SortOrder = 1 };
        var leadStatus = new LeadStatus { Id = Guid.NewGuid(), Code = "ACTIVE", Label = "Active", IsActive = true, SortOrder = 1 };
        db.ServiceTypes.Add(cif);
        db.ShipmentStatuses.Add(packed);
        db.LeadStatuses.Add(leadStatus);

        // One of each scope, so the D-f guard has something real to reject.
        var packingList = new DocumentType { Id = Guid.NewGuid(), Code = "PACKING_LIST", Label = "Packing List", IsActive = true, SortOrder = 5, Scope = DocumentTypeScopes.Shipment };
        var businessLicence = new DocumentType { Id = Guid.NewGuid(), Code = "BUSINESS_LICENCE", Label = "Business Licence", IsActive = true, SortOrder = 1, Scope = DocumentTypeScopes.Vendor };
        db.DocumentTypes.AddRange(packingList, businessLicence);

        var customer = new Customer { Id = Guid.NewGuid(), Name = "Meena", Phone = "+911", ServiceTypeId = cif.Id, StatusId = leadStatus.Id, CreatedAt = DateTime.UtcNow };
        db.Customers.Add(customer);

        var shipment = new Shipment { Id = Guid.NewGuid(), Reference = "SHP-2607-014", CustomerId = customer.Id, ServiceTypeId = cif.Id, StatusId = packed.Id, CreatedAt = DateTime.UtcNow };
        db.Shipments.Add(shipment);

        db.Users.Add(new User { Id = Actor, Name = "Vikram Nair", Email = $"actor-{Guid.NewGuid():N}@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });

        db.SaveChanges();
        return new Fixture(shipment, packingList, businessLicence);
    }

    private static Stream ValidPdfStream(int extraBytes = 100)
    {
        var bytes = new byte[4 + extraBytes];
        "%PDF"u8.CopyTo(bytes);
        return new MemoryStream(bytes);
    }

    private static Stream SpoofedPdfStream()
    {
        // Declares application/pdf at the call site but the bytes are a GIF — extension or
        // content-type checking alone would let this through.
        var bytes = new byte[104];
        "GIF89a"u8.CopyTo(bytes);
        return new MemoryStream(bytes);
    }

    // ---- List ------------------------------------------------------------------------------

    [Fact]
    public async Task ListAsync_UnknownShipment_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _, out _);

        (await sut.ListAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_KnownShipmentWithNoDocuments_ReturnsEmpty()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);

        var result = await sut.ListAsync(f.Shipment.Id);

        result!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_ReturnsNewestFirst()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);

        await sut.UploadDocumentAsync(f.Shipment.Id, ValidPdfStream(), "older.pdf", "application/pdf", 104, f.PackingList.Id, Actor);
        await Task.Delay(10);
        await sut.UploadDocumentAsync(f.Shipment.Id, ValidPdfStream(), "newer.pdf", "application/pdf", 104, f.PackingList.Id, Actor);

        var result = await sut.ListAsync(f.Shipment.Id);

        result!.Items.Select(d => d.OriginalFilename).Should().ContainInOrder("newer.pdf", "older.pdf");
    }

    // ---- Upload ------------------------------------------------------------------------------

    [Fact]
    public async Task UploadDocumentAsync_UnknownShipment_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);

        var result = await sut.UploadDocumentAsync(Guid.NewGuid(), ValidPdfStream(), "x.pdf", "application/pdf", 104, f.PackingList.Id, Actor);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UploadDocumentAsync_UnknownDocumentType_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);

        var act = () => sut.UploadDocumentAsync(f.Shipment.Id, ValidPdfStream(), "x.pdf", "application/pdf", 104, Guid.NewGuid(), Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("documentTypeId");
    }

    [Fact]
    public async Task UploadDocumentAsync_VendorScopedDocumentType_IsRejected()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);

        // The whole reason D-f added the scope column: a "Business Licence" is not a valid
        // shipment reference document, even though it is a perfectly real document_types row.
        var act = () => sut.UploadDocumentAsync(f.Shipment.Id, ValidPdfStream(), "licence.pdf", "application/pdf", 104, f.BusinessLicence.Id, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("documentTypeId");
    }

    [Fact]
    public async Task UploadDocumentAsync_NonPdfContentType_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);

        var act = () => sut.UploadDocumentAsync(f.Shipment.Id, ValidPdfStream(), "x.png", "image/png", 104, f.PackingList.Id, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("file");
    }

    [Fact]
    public async Task UploadDocumentAsync_SpoofedPdfBytes_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);

        var act = () => sut.UploadDocumentAsync(f.Shipment.Id, SpoofedPdfStream(), "fake.pdf", "application/pdf", 104, f.PackingList.Id, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("file");
    }

    [Fact]
    public async Task UploadDocumentAsync_OversizedFile_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out _);

        var act = () => sut.UploadDocumentAsync(f.Shipment.Id, ValidPdfStream(), "big.pdf", "application/pdf", MaxSize + 1, f.PackingList.Id, Actor);

        (await act.Should().ThrowAsync<AppValidationException>()).And.Errors.Should().ContainKey("file");
    }

    [Fact]
    public async Task UploadDocumentAsync_NothingIsStored_WhenValidationFails()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out var storage);

        var act = () => sut.UploadDocumentAsync(f.Shipment.Id, SpoofedPdfStream(), "fake.pdf", "application/pdf", 104, f.PackingList.Id, Actor);
        await act.Should().ThrowAsync<AppValidationException>();

        storage.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        db.ShipmentDocuments.Should().BeEmpty();
    }

    [Fact]
    public async Task UploadDocumentAsync_UsesTheTechSpecPathConvention()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out var storage);

        var result = await sut.UploadDocumentAsync(f.Shipment.Id, ValidPdfStream(), "packing-list.pdf", "application/pdf", 104, f.PackingList.Id, Actor);

        // TECH_SPEC §4.6: /uploads/shipment-docs/{shipmentId}/{filename}
        storage.Verify(s => s.SaveAsync(
            $"shipment-docs/{f.Shipment.Id}/{result!.Id}-packing-list.pdf", It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadDocumentAsync_ReturnsResolvedDocumentTypeAndUploader_AndAuditLogs()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit, out _);

        var result = await sut.UploadDocumentAsync(f.Shipment.Id, ValidPdfStream(), "packing-list.pdf", "application/pdf", 104, f.PackingList.Id, Actor);

        result!.ShipmentId.Should().Be(f.Shipment.Id);
        result.OriginalFilename.Should().Be("packing-list.pdf");
        result.SizeBytes.Should().Be(104);
        result.DocumentType.Code.Should().Be("PACKING_LIST");
        result.DocumentType.Label.Should().Be("Packing List");
        result.UploadedByUserId.Should().Be(Actor);
        result.UploadedByName.Should().Be("Vikram Nair");

        audit.Verify(a => a.LogAsync(Actor, "ShipmentDocumentUploaded", "ShipmentDocument", result.Id.ToString(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ShipmentDocumentDto_HasNoFilePathMember()
    {
        // The single most important property of this DTO: the stored path must never cross the
        // service boundary (TECH_SPEC §8). Asserted structurally so a later field addition trips it.
        typeof(ShipmentDocumentDto).GetProperty("FilePath").Should().BeNull();
    }

    // ---- Download -----------------------------------------------------------------------------

    [Fact]
    public async Task DownloadDocumentAsync_UnknownDocument_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _, out _);

        (await sut.DownloadDocumentAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task DownloadDocumentAsync_ReturnsStreamAndOriginalFilename()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out _, out var storage);
        var uploaded = await sut.UploadDocumentAsync(f.Shipment.Id, ValidPdfStream(), "bl-scan.pdf", "application/pdf", 104, f.PackingList.Id, Actor);
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(ValidPdfStream());

        var result = await sut.DownloadDocumentAsync(uploaded!.Id);

        result!.OriginalFilename.Should().Be("bl-scan.pdf");
        result.ContentType.Should().Be("application/pdf");
    }

    // ---- Delete --------------------------------------------------------------------------------

    [Fact]
    public async Task DeleteDocumentAsync_UnknownDocument_ReturnsFalse()
    {
        using var db = TestDbContextFactory.Create();
        var sut = CreateSut(db, out _, out _);

        (await sut.DeleteDocumentAsync(Guid.NewGuid(), Actor)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteDocumentAsync_RemovesTheStoredFileAsWellAsTheRow()
    {
        using var db = TestDbContextFactory.Create();
        var f = SeedMasterData(db);
        var sut = CreateSut(db, out var audit, out var storage);
        var uploaded = await sut.UploadDocumentAsync(f.Shipment.Id, ValidPdfStream(), "delete-me.pdf", "application/pdf", 104, f.PackingList.Id, Actor);

        (await sut.DeleteDocumentAsync(uploaded!.Id, Actor)).Should().BeTrue();

        storage.Verify(s => s.DeleteAsync($"shipment-docs/{f.Shipment.Id}/{uploaded.Id}-delete-me.pdf", It.IsAny<CancellationToken>()), Times.Once);
        db.ShipmentDocuments.Should().BeEmpty();
        audit.Verify(a => a.LogAsync(Actor, "ShipmentDocumentDeleted", "ShipmentDocument", uploaded.Id.ToString(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
