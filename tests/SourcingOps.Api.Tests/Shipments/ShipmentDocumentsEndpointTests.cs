using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Crm;
using SourcingOps.Application.MasterData;
using SourcingOps.Application.Shipments;

namespace SourcingOps.Api.Tests.Shipments;

/// <summary>
/// Exercises ACTION_PLAN E7-09 / FR-INV-08 over real HTTP: packing list / AWB / BL documents
/// attached under <c>/api/v1/shipments/{id}/documents</c> and served only through the
/// authenticated <c>/api/v1/shipment-documents/{id}/download</c> endpoint — never a public
/// static path (TECH_SPEC §8). Mirrors <c>VendorDocumentsEndpointTests</c> exactly, plus the
/// D-f scope guard, which vendor documents did not need before this pass.
/// </summary>
public class ShipmentDocumentsEndpointTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public ShipmentDocumentsEndpointTests(AdminSeededFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<MasterDataAggregateDto> GetMasterDataAsync()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/master-data");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<MasterDataAggregateDto>())!;
    }

    private async Task<ShipmentDetailDto> CreateShipmentAsync(MasterDataAggregateDto md)
    {
        var customerRequest = new CreateCustomerRequest(
            Name: $"Doc Cust {Guid.NewGuid():N}"[..20], BusinessName: null,
            Phone: $"+9190{Random.Shared.NextInt64(10000000, 99999999)}",
            Email: null, City: null, Region: null, SourceChannel: null,
            ServiceTypeId: md.ServiceTypes.Single(s => s.Code == "CIF").Id,
            StatusId: md.LeadStatuses.First().Id,
            CategoryIds: null, OwnerUserId: null, Tags: null, Notes: null,
            ExternalMarketplace: null, ExternalOrderRef: null, ExternalSupplierName: null,
            ExternalOrderValue: null, ExternalOrderCurrency: null, ExternalOrderDate: null,
            Gstin: null, ConfirmDuplicate: true);
        var customerResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", customerRequest);
        await customerResponse.EnsureSuccessOrThrowWithBodyAsync();
        using var doc = JsonDocument.Parse(await customerResponse.Content.ReadAsStringAsync());
        var customerId = doc.RootElement.GetProperty("id").GetGuid();

        var shipmentRequest = new CreateShipmentRequest(
            customerId, "Surat", md.ServiceTypes.Single(s => s.Code == "CIF").Id, null,
            md.ShipmentStatuses.Single(s => s.Code == "PACKED").Id,
            null, null, null, null, null, null);
        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/shipments", shipmentRequest);
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<ShipmentDetailDto>())!;
    }

    private static HttpContent UploadForm(Guid documentTypeId, string filename = "packing-list.pdf", string contentType = "application/pdf", byte[]? bytes = null)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes ?? Encoding.ASCII.GetBytes("%PDF-1.4 test content for shipment document upload"));
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "file", filename);
        form.Add(new StringContent(documentTypeId.ToString()), "documentTypeId");
        return form;
    }

    private static Guid ShipmentDocTypeId(MasterDataAggregateDto md, string code) =>
        md.DocumentTypes.Single(d => d.Code == code).Id;

    // ---- D-f: the shipment-scoped seed rows exist and are scoped ------------------------------

    [Fact]
    public async Task MasterData_SeedsTheFourShipmentScopedDocumentTypes()
    {
        var md = await GetMasterDataAsync();

        var shipmentScoped = md.DocumentTypes.Where(d => d.Scope == "Shipment").Select(d => d.Code).ToList();
        shipmentScoped.Should().BeEquivalentTo(["PACKING_LIST", "BILL_OF_LADING", "AIRWAY_BILL", "INVOICE"]);
        md.DocumentTypes.Where(d => d.Scope == "Vendor").Select(d => d.Code)
            .Should().BeEquivalentTo(["BUSINESS_LICENCE", "QUALITY_CERTIFICATE", "TEST_REPORT", "OTHER"],
                "existing rows must be backfilled to Vendor, not left null or re-scoped");
        md.DocumentTypes.Should().OnlyContain(d => d.IsSystemDefault, "all eight are seeded defaults and therefore retire-only (N-8)");
    }

    [Fact]
    public async Task Upload_VendorScopedDocumentType_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var shipment = await CreateShipmentAsync(md);
        var vendorScopedId = md.DocumentTypes.Single(d => d.Code == "BUSINESS_LICENCE").Id;

        var response = await _fixture.AssociateClient.PostAsync($"/api/v1/shipments/{shipment.Id}/documents", UploadForm(vendorScopedId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task VendorUpload_ShipmentScopedDocumentType_Returns400ProblemDetails()
    {
        // The other half of D-f: the guard has to work in both directions, or the vendor
        // dropdown would still accept "Packing List".
        var md = await GetMasterDataAsync();
        var vendorRequest = new SourcingOps.Application.Vendors.CreateVendorRequest(
            Name: $"Scope-Vendor-{Guid.NewGuid():N}", ContactPerson: null, Phone: null, Email: null, Region: null,
            StatusId: md.VendorStatuses.Single(s => s.Code == "ACTIVE").Id, CategoryIds: null,
            Moq: null, LeadTime: null, PaymentTerms: null, ReliabilityRating: null, Notes: null);
        var vendorResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/vendors", vendorRequest);
        await vendorResponse.EnsureSuccessOrThrowWithBodyAsync();
        var vendor = (await vendorResponse.Content.ReadFromJsonAsync<SourcingOps.Application.Vendors.VendorDetailDto>())!;

        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4 x"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(content, "file", "x.pdf");
        form.Add(new StringContent(ShipmentDocTypeId(md, "PACKING_LIST").ToString()), "docTypeId");

        var response = await _fixture.AssociateClient.PostAsync($"/api/v1/vendors/{vendor.Id}/documents", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- Upload -------------------------------------------------------------------------------

    [Fact]
    public async Task Upload_ValidPdf_Returns201_WithEmbeddedDocumentType_NeverExposesFilePath()
    {
        var md = await GetMasterDataAsync();
        var shipment = await CreateShipmentAsync(md);

        var response = await _fixture.AssociateClient.PostAsync($"/api/v1/shipments/{shipment.Id}/documents",
            UploadForm(ShipmentDocTypeId(md, "PACKING_LIST")));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<ShipmentDocumentDto>())!;
        body.OriginalFilename.Should().Be("packing-list.pdf");
        body.ShipmentId.Should().Be(shipment.Id);
        body.DocumentType.Code.Should().Be("PACKING_LIST");
        body.SizeBytes.Should().BeGreaterThan(0);
        body.UploadedByName.Should().NotBeNullOrWhiteSpace();
        (await response.Content.ReadAsStringAsync()).Should().NotContain("filePath",
            "the DTO must never expose the stored path, matching VendorDocumentDto's convention");
    }

    [Fact]
    public async Task Upload_UnknownShipment_Returns404()
    {
        var md = await GetMasterDataAsync();

        var response = await _fixture.AssociateClient.PostAsync($"/api/v1/shipments/{Guid.NewGuid()}/documents",
            UploadForm(ShipmentDocTypeId(md, "PACKING_LIST")));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_UnknownDocumentType_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var shipment = await CreateShipmentAsync(md);

        var response = await _fixture.AssociateClient.PostAsync($"/api/v1/shipments/{shipment.Id}/documents", UploadForm(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Upload_NonPdfContentType_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var shipment = await CreateShipmentAsync(md);

        var response = await _fixture.AssociateClient.PostAsync($"/api/v1/shipments/{shipment.Id}/documents",
            UploadForm(ShipmentDocTypeId(md, "PACKING_LIST"), contentType: "image/png"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_SpoofedPdfBytes_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var shipment = await CreateShipmentAsync(md);

        // Declares application/pdf, but the bytes are a GIF. Extension or content-type checking
        // alone would pass this — only the magic-byte check catches it.
        var response = await _fixture.AssociateClient.PostAsync($"/api/v1/shipments/{shipment.Id}/documents",
            UploadForm(ShipmentDocTypeId(md, "PACKING_LIST"), bytes: Encoding.ASCII.GetBytes("GIF89a not really a pdf")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    // ---- List ------------------------------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsUploadedDocuments()
    {
        var md = await GetMasterDataAsync();
        var shipment = await CreateShipmentAsync(md);
        var uploadResponse = await _fixture.AssociateClient.PostAsync($"/api/v1/shipments/{shipment.Id}/documents",
            UploadForm(ShipmentDocTypeId(md, "BILL_OF_LADING"), "bl-scan.pdf"));
        await uploadResponse.EnsureSuccessOrThrowWithBodyAsync();

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/shipments/{shipment.Id}/documents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<ShipmentDocumentListResultDto>())!;
        body.Items.Should().ContainSingle(d => d.OriginalFilename == "bl-scan.pdf" && d.DocumentType.Code == "BILL_OF_LADING");
    }

    [Fact]
    public async Task List_UnknownShipment_Returns404()
    {
        (await _fixture.AssociateClient.GetAsync($"/api/v1/shipments/{Guid.NewGuid()}/documents")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ShipmentDetail_EmbedsItsDocuments()
    {
        var md = await GetMasterDataAsync();
        var shipment = await CreateShipmentAsync(md);
        var uploadResponse = await _fixture.AssociateClient.PostAsync($"/api/v1/shipments/{shipment.Id}/documents",
            UploadForm(ShipmentDocTypeId(md, "AIRWAY_BILL"), "awb.pdf"));
        await uploadResponse.EnsureSuccessOrThrowWithBodyAsync();

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/shipments/{shipment.Id}");

        var body = (await response.Content.ReadFromJsonAsync<ShipmentDetailDto>())!;
        body.Documents.Should().ContainSingle().Which.DocumentType.Code.Should().Be("AIRWAY_BILL");
    }

    // ---- Download (separate /shipment-documents resource root) ---------------------------------------

    private async Task<Guid> UploadDocumentAsync(MasterDataAggregateDto md, byte[] pdfContent, string filename = "doc.pdf")
    {
        var shipment = await CreateShipmentAsync(md);
        var response = await _fixture.AssociateClient.PostAsync($"/api/v1/shipments/{shipment.Id}/documents",
            UploadForm(ShipmentDocTypeId(md, "PACKING_LIST"), filename, bytes: pdfContent));
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<ShipmentDocumentDto>())!.Id;
    }

    [Fact]
    public async Task Download_KnownDocument_ReturnsPdfBytes_WithOriginalFilename()
    {
        var md = await GetMasterDataAsync();
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4 round trip shipment document content");
        var documentId = await UploadDocumentAsync(md, pdfBytes, "roundtrip.pdf");

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/shipment-documents/{documentId}/download");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition?.FileName.Should().Contain("roundtrip.pdf");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(pdfBytes);
    }

    [Fact]
    public async Task Download_Anonymous_Returns401_NotTheFile()
    {
        var md = await GetMasterDataAsync();
        var documentId = await UploadDocumentAsync(md, Encoding.ASCII.GetBytes("%PDF-1.4 secret"), "secret.pdf");

        using var anonymous = _fixture.Factory.CreateClient();
        var response = await anonymous.GetAsync($"/api/v1/shipment-documents/{documentId}/download");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Download_UnknownDocument_Returns404()
    {
        (await _fixture.AssociateClient.GetAsync($"/api/v1/shipment-documents/{Guid.NewGuid()}/download")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StaticPathProbe_IsNotServed()
    {
        var md = await GetMasterDataAsync();
        var shipment = await CreateShipmentAsync(md);
        var uploadResponse = await _fixture.AssociateClient.PostAsync($"/api/v1/shipments/{shipment.Id}/documents",
            UploadForm(ShipmentDocTypeId(md, "PACKING_LIST"), "static-probe.pdf"));
        await uploadResponse.EnsureSuccessOrThrowWithBodyAsync();
        var uploaded = (await uploadResponse.Content.ReadFromJsonAsync<ShipmentDocumentDto>())!;

        using var anonymous = _fixture.Factory.CreateClient();
        var response = await anonymous.GetAsync($"/uploads/shipment-docs/{shipment.Id}/{uploaded.Id}-static-probe.pdf");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "TECH_SPEC §8: documents are never reachable from a public static path");
    }

    // ---- Delete ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_KnownDocument_Returns204_ThenDownloadReturns404()
    {
        var md = await GetMasterDataAsync();
        var documentId = await UploadDocumentAsync(md, Encoding.ASCII.GetBytes("%PDF-1.4 delete-me"), "delete-me.pdf");

        (await _fixture.AssociateClient.DeleteAsync($"/api/v1/shipment-documents/{documentId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _fixture.AssociateClient.GetAsync($"/api/v1/shipment-documents/{documentId}/download")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_UnknownDocument_Returns404()
    {
        (await _fixture.AssociateClient.DeleteAsync($"/api/v1/shipment-documents/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteShipment_WithDocumentsStillAttached_Returns400_RatherThanOrphaningFiles()
    {
        var md = await GetMasterDataAsync();
        var shipment = await CreateShipmentAsync(md);
        var uploadResponse = await _fixture.AssociateClient.PostAsync($"/api/v1/shipments/{shipment.Id}/documents",
            UploadForm(ShipmentDocTypeId(md, "PACKING_LIST"), "attached.pdf"));
        await uploadResponse.EnsureSuccessOrThrowWithBodyAsync();

        var response = await _fixture.AssociateClient.DeleteAsync($"/api/v1/shipments/{shipment.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    // ---- DoD: 403 per endpoint --------------------------------------------------------------------------

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnListDocuments()
    {
        var client = await GetNoPermissionClientAsync();
        (await client.GetAsync($"/api/v1/shipments/{Guid.NewGuid()}/documents")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnUploadDocument()
    {
        var client = await GetNoPermissionClientAsync();
        var response = await client.PostAsync($"/api/v1/shipments/{Guid.NewGuid()}/documents", UploadForm(Guid.NewGuid()));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnDownload()
    {
        var client = await GetNoPermissionClientAsync();
        (await client.GetAsync($"/api/v1/shipment-documents/{Guid.NewGuid()}/download")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnDelete()
    {
        var client = await GetNoPermissionClientAsync();
        (await client.DeleteAsync($"/api/v1/shipment-documents/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> GetNoPermissionClientAsync()
    {
        var auth = await AdminApiTestHelpers.ProvisionActiveUserAsync(
            _fixture.Factory, _fixture.AdminClient, roleIds: [], "No Perms Ship Doc User", Guid.NewGuid().ToString("N")[..8], "NoPerms-Changed-Pw1!");
        return _fixture.Factory.CreateClient().WithBearer(auth.AccessToken);
    }
}
