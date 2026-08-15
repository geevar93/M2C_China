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
/// ACTION_PLAN E9-10. This suite is deliberately weighted towards the failure paths, because
/// every one of them is a security property rather than an ergonomic one: an expired, revoked,
/// unknown or dangling token must all resolve to the same nothing, and none of them may reveal
/// that the token was ever real.
/// </summary>
public class DocumentShareLinkServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private sealed record Sut(
        DocumentShareLinkService Service,
        Mock<IAuditLogger> Audit,
        Mock<IFileStorage> Storage,
        TestShareTokenFactory Tokens,
        DispatchOptions Options);

    private static Sut CreateSut(AppDbContext db, DispatchOptions? options = null)
    {
        var audit = new Mock<IAuditLogger>();
        var storage = new Mock<IFileStorage>();
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream("%PDF-1.4 shared"u8.ToArray()));

        var tokens = new TestShareTokenFactory();
        var resolved = options ?? new DispatchOptions { PublicBaseUrl = "https://ops.example.com" };
        return new Sut(
            new DocumentShareLinkService(db, storage.Object, tokens, audit.Object, resolved),
            audit, storage, tokens, resolved);
    }

    private sealed record Fixture(CatalogDocument Document, Invoice Invoice, User Staff);

    private static Fixture Seed(AppDbContext db)
    {
        var staff = new User { Id = Actor, Name = "Acting Staff", Email = $"a-{Guid.NewGuid():N}@example.com", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Users.Add(staff);

        var vendorStatus = new VendorStatus { Id = Guid.NewGuid(), Code = "ACTIVE", Label = "Active", IsActive = true, SortOrder = 1 };
        db.VendorStatuses.Add(vendorStatus);
        var category = new Category { Id = Guid.NewGuid(), Name = "Jewellery", IsActive = true, SortOrder = 1 };
        db.Categories.Add(category);
        var vendor = new Vendor { Id = Guid.NewGuid(), Name = "Golden Dragon", StatusId = vendorStatus.Id, CreatedAt = DateTime.UtcNow };
        db.Vendors.Add(vendor);

        var section = new CatalogSection
        {
            Id = Guid.NewGuid(), VendorId = vendor.Id, Title = "Spring 2026 Collection",
            CategoryId = category.Id, CreatedAt = DateTime.UtcNow
        };
        db.CatalogSections.Add(section);

        var document = new CatalogDocument
        {
            Id = Guid.NewGuid(), CatalogSectionId = section.Id, FilePath = "catalog-docs/x/spring.pdf",
            OriginalFilename = "spring-2026.pdf", SizeBytes = 2048, IsLatest = true,
            UploadedByUserId = staff.Id, UploadedAt = DateTime.UtcNow
        };
        db.CatalogDocuments.Add(document);

        var serviceType = new ServiceType { Id = Guid.NewGuid(), Code = "CIF", Label = "CIF", IsActive = true, SortOrder = 1 };
        db.ServiceTypes.Add(serviceType);
        var leadStatus = new LeadStatus { Id = Guid.NewGuid(), Code = "NEW", Label = "New", IsActive = true, SortOrder = 1 };
        db.LeadStatuses.Add(leadStatus);
        var customer = new Customer
        {
            Id = Guid.NewGuid(), Name = "Kundan Traders", Phone = "+919825041122",
            ServiceTypeId = serviceType.Id, StatusId = leadStatus.Id, CreatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);

        var invoiceStatus = new InvoiceStatus { Id = Guid.NewGuid(), Code = "ISSUED", Label = "Issued", IsActive = true, SortOrder = 2 };
        db.InvoiceStatuses.Add(invoiceStatus);
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(), CustomerId = customer.Id, InvoiceNumber = "INV-2608-007",
            InvoiceDate = DateTime.UtcNow, Amount = 5000m, TaxAmount = 900m, Currency = "INR",
            StatusId = invoiceStatus.Id, PdfFilePath = "invoices/inv.pdf",
            CreatedByUserId = staff.Id, CreatedAt = DateTime.UtcNow
        };
        db.Invoices.Add(invoice);

        db.SaveChanges();
        return new Fixture(document, invoice, staff);
    }

    // ---- Mint ---------------------------------------------------------------------------

    [Fact]
    public async Task MintAsync_CatalogDocument_CreatesALiveLink_AndAudits()
    {
        using var db = TestDbContextFactory.Create();
        var f = Seed(db);
        var sut = CreateSut(db);

        var result = await sut.Service.MintAsync("CatalogDocument", f.Document.Id, Actor);

        result.Url.Should().Be($"https://ops.example.com/api/v1/shared-documents/{sut.Tokens.LastToken}");
        result.ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddHours(48), TimeSpan.FromMinutes(1));

        var stored = await db.DocumentShareLinks.SingleAsync();
        stored.Id.Should().Be(result.Id);
        stored.TokenHash.Should().Be(sut.Tokens.Hash(sut.Tokens.LastToken));
        stored.CreatedByUserId.Should().Be(Actor);

        sut.Audit.Verify(a => a.LogAsync(Actor, "ShareLinkCreated", "DocumentShareLink", stored.Id.ToString(),
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MintAsync_ConfiguredLifetime_IsHonoured()
    {
        using var db = TestDbContextFactory.Create();
        var f = Seed(db);
        var sut = CreateSut(db, new DispatchOptions { PublicBaseUrl = "https://ops.example.com", ShareLinkLifetimeHours = 6 });

        var result = await sut.Service.MintAsync("CatalogDocument", f.Document.Id, Actor);

        result.ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddHours(6), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task MintAsync_UnknownTargetId_Throws_AndWritesNoRow()
    {
        using var db = TestDbContextFactory.Create();
        Seed(db);
        var sut = CreateSut(db);

        var act = async () => await sut.Service.MintAsync("CatalogDocument", Guid.NewGuid(), Actor);

        await act.Should().ThrowAsync<AppValidationException>();
        (await db.DocumentShareLinks.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// A target type outside <c>DocumentShareTargetTypes.All</c> is rejected before anything is
    /// written. Without this a typo'd or attacker-influenced type string would create a row that
    /// resolves to nothing — a link that looks minted and silently never works.
    /// </summary>
    [Fact]
    public async Task MintAsync_UnsupportedTargetType_Throws()
    {
        using var db = TestDbContextFactory.Create();
        var f = Seed(db);
        var sut = CreateSut(db);

        var act = async () => await sut.Service.MintAsync("VendorDocument", f.Document.Id, Actor);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    // ---- Resolve: the happy path and repeat use ------------------------------------------

    [Fact]
    public async Task ResolveAsync_LiveToken_ReturnsTheFile_CountsTheAccess_AndAuditsAnonymously()
    {
        using var db = TestDbContextFactory.Create();
        var f = Seed(db);
        var sut = CreateSut(db);
        var minted = await sut.Service.MintAsync("CatalogDocument", f.Document.Id, Actor);
        var token = sut.Tokens.LastToken;

        var download = await sut.Service.ResolveAsync(token);

        download.Should().NotBeNull();
        download!.FileName.Should().Be("spring-2026.pdf");
        download.ContentType.Should().Be("application/pdf");

        var stored = await db.DocumentShareLinks.SingleAsync();
        stored.AccessCount.Should().Be(1);
        stored.LastAccessedAt.Should().NotBeNull();

        // userId null, deliberately: the staff member shared the link, they did not open it.
        sut.Audit.Verify(a => a.LogAsync(null, "ShareLinkAccessed", "DocumentShareLink", minted.Id.ToString(),
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The single-use question, pinned. Repeat opens inside the window are the expected case —
    /// see <see cref="DocumentShareLinkService"/> on WhatsApp's own link-preview prefetch, which
    /// would burn a single-use token before the human ever tapped it.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_SameTokenThreeTimes_AllSucceed_AndAccessCountReachesThree()
    {
        using var db = TestDbContextFactory.Create();
        var f = Seed(db);
        var sut = CreateSut(db);
        await sut.Service.MintAsync("CatalogDocument", f.Document.Id, Actor);
        var token = sut.Tokens.LastToken;

        (await sut.Service.ResolveAsync(token)).Should().NotBeNull();
        (await sut.Service.ResolveAsync(token)).Should().NotBeNull();
        (await sut.Service.ResolveAsync(token)).Should().NotBeNull();

        (await db.DocumentShareLinks.SingleAsync()).AccessCount.Should().Be(3);
    }

    [Fact]
    public async Task ResolveAsync_IssuedInvoice_ServesThePdfNamedAfterTheInvoiceNumber()
    {
        using var db = TestDbContextFactory.Create();
        var f = Seed(db);
        var sut = CreateSut(db);
        await sut.Service.MintAsync("Invoice", f.Invoice.Id, Actor);

        var download = await sut.Service.ResolveAsync(sut.Tokens.LastToken);

        download!.FileName.Should().Be("INV-2608-007.pdf");
    }

    // ---- Resolve: every failure path returns the same nothing ----------------------------

    [Fact]
    public async Task ResolveAsync_UnknownToken_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var f = Seed(db);
        var sut = CreateSut(db);
        await sut.Service.MintAsync("CatalogDocument", f.Document.Id, Actor);

        (await sut.Service.ResolveAsync("not-a-real-token")).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveAsync_BlankToken_ReturnsNull_WithoutQueryingAnything(string token)
    {
        using var db = TestDbContextFactory.Create();
        Seed(db);
        var sut = CreateSut(db);

        (await sut.Service.ResolveAsync(token)).Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ExpiredToken_ReturnsNull_AndDoesNotCountTheAttempt()
    {
        using var db = TestDbContextFactory.Create();
        var f = Seed(db);
        var sut = CreateSut(db);
        await sut.Service.MintAsync("CatalogDocument", f.Document.Id, Actor);
        var token = sut.Tokens.LastToken;

        var link = await db.DocumentShareLinks.SingleAsync();
        link.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        (await sut.Service.ResolveAsync(token)).Should().BeNull();
        (await db.DocumentShareLinks.SingleAsync()).AccessCount.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_RevokedToken_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var f = Seed(db);
        var sut = CreateSut(db);
        var minted = await sut.Service.MintAsync("CatalogDocument", f.Document.Id, Actor);
        var token = sut.Tokens.LastToken;

        (await sut.Service.RevokeAsync(minted.Id, Actor)).Should().Be(ShareLinkRevokeResult.Revoked);

        (await sut.Service.ResolveAsync(token)).Should().BeNull();
    }

    /// <summary>
    /// A link whose target row was deleted after minting. Resolves to the same null as an
    /// unknown token rather than throwing — an anonymous caller must never be able to tell the
    /// difference between "never existed" and "existed and is gone".
    /// </summary>
    [Fact]
    public async Task ResolveAsync_DanglingTarget_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var f = Seed(db);
        var sut = CreateSut(db);
        await sut.Service.MintAsync("CatalogDocument", f.Document.Id, Actor);
        var token = sut.Tokens.LastToken;

        db.CatalogDocuments.Remove(await db.CatalogDocuments.SingleAsync(d => d.Id == f.Document.Id));
        await db.SaveChangesAsync();

        (await sut.Service.ResolveAsync(token)).Should().BeNull();
    }

    /// <summary>
    /// The row is fine but the file is gone from storage — a restore gap or a manual deletion.
    /// Must be a 404, not a 500 leaking a FileNotFoundException path to an anonymous caller.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_FileMissingFromStorage_ReturnsNull_NotAnException()
    {
        using var db = TestDbContextFactory.Create();
        var f = Seed(db);
        var sut = CreateSut(db);
        await sut.Service.MintAsync("CatalogDocument", f.Document.Id, Actor);
        var token = sut.Tokens.LastToken;

        sut.Storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("gone", "catalog-docs/x/spring.pdf"));

        (await sut.Service.ResolveAsync(token)).Should().BeNull();
        (await db.DocumentShareLinks.SingleAsync()).AccessCount.Should().Be(0);
    }

    // ---- Revoke ---------------------------------------------------------------------------

    [Fact]
    public async Task RevokeAsync_UnknownId_ReturnsNotFound()
    {
        using var db = TestDbContextFactory.Create();
        Seed(db);
        var sut = CreateSut(db);

        (await sut.Service.RevokeAsync(Guid.NewGuid(), Actor)).Should().Be(ShareLinkRevokeResult.NotFound);
    }

    [Fact]
    public async Task RevokeAsync_LiveLink_StampsRevokedAt_AndAudits()
    {
        using var db = TestDbContextFactory.Create();
        var f = Seed(db);
        var sut = CreateSut(db);
        var minted = await sut.Service.MintAsync("CatalogDocument", f.Document.Id, Actor);

        var result = await sut.Service.RevokeAsync(minted.Id, Actor);

        result.Should().Be(ShareLinkRevokeResult.Revoked);
        (await db.DocumentShareLinks.SingleAsync()).RevokedAt.Should().NotBeNull();
        sut.Audit.Verify(a => a.LogAsync(Actor, "ShareLinkRevoked", "DocumentShareLink", minted.Id.ToString(),
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_AlreadyRevoked_IsIdempotent()
    {
        using var db = TestDbContextFactory.Create();
        var f = Seed(db);
        var sut = CreateSut(db);
        var minted = await sut.Service.MintAsync("CatalogDocument", f.Document.Id, Actor);
        await sut.Service.RevokeAsync(minted.Id, Actor);

        var second = await sut.Service.RevokeAsync(minted.Id, Actor);

        second.Should().Be(ShareLinkRevokeResult.AlreadyInactive);
        // Not re-audited and not re-stamped — the first revoke's timestamp is the real one.
        sut.Audit.Verify(a => a.LogAsync(Actor, "ShareLinkRevoked", "DocumentShareLink", minted.Id.ToString(),
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_AlreadyExpired_ReportsAlreadyInactive()
    {
        using var db = TestDbContextFactory.Create();
        var f = Seed(db);
        var sut = CreateSut(db);
        var minted = await sut.Service.MintAsync("CatalogDocument", f.Document.Id, Actor);

        var link = await db.DocumentShareLinks.SingleAsync();
        link.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        (await sut.Service.RevokeAsync(minted.Id, Actor)).Should().Be(ShareLinkRevokeResult.AlreadyInactive);
    }
}
