using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Catalog;
using SourcingOps.Application.MasterData;
using SourcingOps.Application.Vendors;

namespace SourcingOps.Api.Tests.Catalog;

/// <summary>
/// Exercises ACTION_PLAN E6-04 over real HTTP: documents are served only through this
/// authenticated, permission-checked endpoint (TECH_SPEC §8) — never a public static path.
/// </summary>
public class CatalogDocumentsEndpointTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public CatalogDocumentsEndpointTests(AdminSeededFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<MasterDataAggregateDto> GetMasterDataAsync()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/master-data");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<MasterDataAggregateDto>())!;
    }

    private async Task<Guid> CreateSectionWithDocumentAsync(byte[] pdfContent, string filename = "catalog.pdf")
    {
        var md = await GetMasterDataAsync();
        var vendorRequest = new CreateVendorRequest(
            Name: $"Doc-Vendor-{Guid.NewGuid():N}", ContactPerson: null, Phone: null, Email: null,
            Region: "Yiwu, Zhejiang", StatusId: md.VendorStatuses.Single(s => s.Code == "ACTIVE").Id,
            CategoryIds: null, Moq: null, LeadTime: null, PaymentTerms: null, ReliabilityRating: null, Notes: null);
        var vendorResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/vendors", vendorRequest);
        await vendorResponse.EnsureSuccessOrThrowWithBodyAsync();
        var vendor = (await vendorResponse.Content.ReadFromJsonAsync<VendorDetailDto>())!;

        var sectionResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/catalog-sections",
            new CreateCatalogSectionRequest(vendor.Id, "Download Test Section", md.Categories.First().Id, null));
        await sectionResponse.EnsureSuccessOrThrowWithBodyAsync();
        var section = (await sectionResponse.Content.ReadFromJsonAsync<CatalogSectionDto>())!;

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfContent);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", filename);
        var uploadResponse = await _fixture.AssociateClient.PostAsync($"/api/v1/catalog-sections/{section.Id}/documents", form);
        await uploadResponse.EnsureSuccessOrThrowWithBodyAsync();
        var document = (await uploadResponse.Content.ReadFromJsonAsync<CatalogDocumentDto>())!;

        return document.Id;
    }

    [Fact]
    public async Task Download_KnownDocument_ReturnsPdfBytes_WithOriginalFilename()
    {
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4 round trip content");
        var documentId = await CreateSectionWithDocumentAsync(pdfBytes, "roundtrip.pdf");

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/catalog-documents/{documentId}/download");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition?.FileName.Should().Contain("roundtrip.pdf");
        var downloaded = await response.Content.ReadAsByteArrayAsync();
        downloaded.Should().Equal(pdfBytes);
    }

    [Fact]
    public async Task Download_UnknownDocument_Returns404()
    {
        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/catalog-documents/{Guid.NewGuid()}/download");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnDownload()
    {
        var auth = await AdminApiTestHelpers.ProvisionActiveUserAsync(
            _fixture.Factory, _fixture.AdminClient, roleIds: [], "No Perms Download User", Guid.NewGuid().ToString("N")[..8], "NoPerms-Changed-Pw1!");
        var client = _fixture.Factory.CreateClient().WithBearer(auth.AccessToken);

        var response = await client.GetAsync($"/api/v1/catalog-documents/{Guid.NewGuid()}/download");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
