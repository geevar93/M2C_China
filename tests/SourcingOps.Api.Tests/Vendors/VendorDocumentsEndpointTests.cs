using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.MasterData;
using SourcingOps.Application.Vendors;

namespace SourcingOps.Api.Tests.Vendors;

/// <summary>
/// Exercises ACTION_PLAN E5-07 / FR-VEN-07 over real HTTP: vendor-level documents (licence,
/// quality certs) attached under <c>/api/v1/vendors/{id}/documents</c> and served only
/// through the authenticated <c>/api/v1/vendor-documents/{id}/download</c> endpoint — never a
/// public static path (TECH_SPEC §8). Mirrors <c>CatalogSectionsEndpointTests</c> /
/// <c>CatalogDocumentsEndpointTests</c>'s structure through <see cref="AdminSeededFixture"/>'s
/// already-provisioned Super Admin/Associate clients — real Postgres (Testcontainers), real
/// JWT auth pipeline, real permission policies, real local-disk file storage.
/// </summary>
public class VendorDocumentsEndpointTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public VendorDocumentsEndpointTests(AdminSeededFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<MasterDataAggregateDto> GetMasterDataAsync()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/master-data");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<MasterDataAggregateDto>())!;
    }

    private async Task<VendorDetailDto> CreateVendorAsync(MasterDataAggregateDto md, string namePrefix)
    {
        var request = new CreateVendorRequest(
            Name: $"{namePrefix}-{Guid.NewGuid():N}", ContactPerson: null, Phone: null, Email: null,
            Region: "Yiwu, Zhejiang", StatusId: md.VendorStatuses.Single(s => s.Code == "ACTIVE").Id,
            CategoryIds: null, Moq: null, LeadTime: null, PaymentTerms: null, ReliabilityRating: null, Notes: null);
        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/vendors", request);
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<VendorDetailDto>())!;
    }

    private static HttpContent UploadForm(Guid docTypeId, string filename = "licence.pdf", string contentType = "application/pdf")
    {
        var form = new MultipartFormDataContent();
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4 test content for vendor document upload");
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", filename);
        form.Add(new StringContent(docTypeId.ToString()), "docTypeId");
        return form;
    }

    // ---- Upload (nested under a vendor) --------------------------------------------------

    [Fact]
    public async Task Upload_ValidPdf_Returns201_WithEmbeddedDocType_NeverExposesFilePath()
    {
        var md = await GetMasterDataAsync();
        var vendor = await CreateVendorAsync(md, "Upload-Vendor");
        var docTypeId = md.DocumentTypes.Single(d => d.Code == "BUSINESS_LICENCE").Id;

        var response = await _fixture.AssociateClient.PostAsync($"/api/v1/vendors/{vendor.Id}/documents", UploadForm(docTypeId));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<VendorDocumentDto>();
        body!.OriginalFilename.Should().Be("licence.pdf");
        body.VendorId.Should().Be(vendor.Id);
        body.DocType.Code.Should().Be("BUSINESS_LICENCE");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("filePath", "the DTO must never expose the stored path, matching CatalogDocumentDto's convention");
    }

    [Fact]
    public async Task Upload_UnknownVendorId_Returns404()
    {
        var md = await GetMasterDataAsync();
        var docTypeId = md.DocumentTypes.First().Id;

        var response = await _fixture.AssociateClient.PostAsync($"/api/v1/vendors/{Guid.NewGuid()}/documents", UploadForm(docTypeId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_UnknownDocTypeId_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var vendor = await CreateVendorAsync(md, "BadDocType-Vendor");

        var response = await _fixture.AssociateClient.PostAsync($"/api/v1/vendors/{vendor.Id}/documents", UploadForm(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Upload_NonPdfContentType_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var vendor = await CreateVendorAsync(md, "BadUpload-Vendor");
        var docTypeId = md.DocumentTypes.First().Id;

        var response = await _fixture.AssociateClient.PostAsync($"/api/v1/vendors/{vendor.Id}/documents", UploadForm(docTypeId, contentType: "image/png"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    // ---- List (nested under a vendor) ----------------------------------------------------

    [Fact]
    public async Task List_UnknownVendorId_Returns404()
    {
        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/vendors/{Guid.NewGuid()}/documents");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_ReturnsUploadedDocuments()
    {
        var md = await GetMasterDataAsync();
        var vendor = await CreateVendorAsync(md, "List-Vendor");
        var docTypeId = md.DocumentTypes.Single(d => d.Code == "QUALITY_CERTIFICATE").Id;
        var uploadResponse = await _fixture.AssociateClient.PostAsync($"/api/v1/vendors/{vendor.Id}/documents", UploadForm(docTypeId, "cert.pdf"));
        await uploadResponse.EnsureSuccessOrThrowWithBodyAsync();

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/vendors/{vendor.Id}/documents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<VendorDocumentListResultDto>();
        body!.Items.Should().ContainSingle(d => d.OriginalFilename == "cert.pdf" && d.DocType.Code == "QUALITY_CERTIFICATE");
    }

    // ---- Download (separate /vendor-documents resource root) ----------------------------

    private async Task<Guid> UploadDocumentAsync(MasterDataAggregateDto md, string namePrefix, byte[] pdfContent, string filename = "doc.pdf")
    {
        var vendor = await CreateVendorAsync(md, namePrefix);
        var docTypeId = md.DocumentTypes.First().Id;
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfContent);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", filename);
        form.Add(new StringContent(docTypeId.ToString()), "docTypeId");
        var response = await _fixture.AssociateClient.PostAsync($"/api/v1/vendors/{vendor.Id}/documents", form);
        await response.EnsureSuccessOrThrowWithBodyAsync();
        var body = await response.Content.ReadFromJsonAsync<VendorDocumentDto>();
        return body!.Id;
    }

    [Fact]
    public async Task Download_KnownDocument_ReturnsPdfBytes_WithOriginalFilename()
    {
        var md = await GetMasterDataAsync();
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4 round trip vendor document content");
        var documentId = await UploadDocumentAsync(md, "Download-Vendor", pdfBytes, "roundtrip.pdf");

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/vendor-documents/{documentId}/download");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition?.FileName.Should().Contain("roundtrip.pdf");
        var downloaded = await response.Content.ReadAsByteArrayAsync();
        downloaded.Should().Equal(pdfBytes);
    }

    [Fact]
    public async Task Download_UnknownDocument_Returns404()
    {
        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/vendor-documents/{Guid.NewGuid()}/download");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Delete (separate /vendor-documents resource root) -------------------------------

    [Fact]
    public async Task Delete_KnownDocument_Returns204_ThenDownloadReturns404()
    {
        var md = await GetMasterDataAsync();
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4 delete-me content");
        var documentId = await UploadDocumentAsync(md, "Delete-Vendor", pdfBytes, "delete-me.pdf");

        var deleteResponse = await _fixture.AssociateClient.DeleteAsync($"/api/v1/vendor-documents/{documentId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var downloadResponse = await _fixture.AssociateClient.GetAsync($"/api/v1/vendor-documents/{documentId}/download");
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_UnknownDocument_Returns404()
    {
        var response = await _fixture.AssociateClient.DeleteAsync($"/api/v1/vendor-documents/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- DoD: every permission-gated endpoint needs a 403-for-missing-permission test ----

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnListDocuments()
    {
        var client = await GetNoPermissionClientAsync();

        var response = await client.GetAsync($"/api/v1/vendors/{Guid.NewGuid()}/documents");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnUploadDocument()
    {
        var client = await GetNoPermissionClientAsync();

        var response = await client.PostAsync($"/api/v1/vendors/{Guid.NewGuid()}/documents", UploadForm(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnDownload()
    {
        var client = await GetNoPermissionClientAsync();

        var response = await client.GetAsync($"/api/v1/vendor-documents/{Guid.NewGuid()}/download");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnDelete()
    {
        var client = await GetNoPermissionClientAsync();

        var response = await client.DeleteAsync($"/api/v1/vendor-documents/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> GetNoPermissionClientAsync()
    {
        var auth = await AdminApiTestHelpers.ProvisionActiveUserAsync(
            _fixture.Factory, _fixture.AdminClient, roleIds: [], "No Perms Vendor Doc User", Guid.NewGuid().ToString("N")[..8], "NoPerms-Changed-Pw1!");
        return _fixture.Factory.CreateClient().WithBearer(auth.AccessToken);
    }
}
