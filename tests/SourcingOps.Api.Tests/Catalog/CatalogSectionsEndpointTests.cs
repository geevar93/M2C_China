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
/// Exercises ACTION_PLAN E6-01…E6-07 over real HTTP against the coordinator's binding
/// `/api/v1/catalog-sections` contract, through <see cref="AdminSeededFixture"/>'s
/// already-provisioned Super Admin/Associate clients — real Postgres (Testcontainers), real
/// JWT auth pipeline, real permission policies, real local-disk file storage.
/// </summary>
public class CatalogSectionsEndpointTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public CatalogSectionsEndpointTests(AdminSeededFixture fixture)
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

    private static HttpContent UploadForm(string filename = "catalog.pdf", string contentType = "application/pdf", string? versionLabel = null)
    {
        var form = new MultipartFormDataContent();
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4 test content for catalog document upload");
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", filename);
        if (versionLabel is not null)
        {
            form.Add(new StringContent(versionLabel), "versionLabel");
        }
        return form;
    }

    // ---- Create (E6-01) -----------------------------------------------------------------

    [Fact]
    public async Task Create_ValidSection_Returns201_WithNormalizedTags()
    {
        var md = await GetMasterDataAsync();
        var vendor = await CreateVendorAsync(md, "Golden-Dragon");
        var request = new CreateCatalogSectionRequest(vendor.Id, "Spring 2026 Collection", md.Categories.Single(c => c.Name == "Jewellery").Id, ["new arrivals", "New Arrivals"]);

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/catalog-sections", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CatalogSectionDto>();
        body!.Title.Should().Be("Spring 2026 Collection");
        body.VendorName.Should().Be(vendor.Name);
        body.Tags.Should().Equal("new arrivals");
        body.Documents.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_UnknownVendorId_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var request = new CreateCatalogSectionRequest(Guid.NewGuid(), "Section", md.Categories.First().Id, null);

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/catalog-sections", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    // ---- List / search / filter (E6-05, E6-07) -----------------------------------------

    [Fact]
    public async Task List_CrossVendorFilters_ReturnMatchingResults()
    {
        var md = await GetMasterDataAsync();
        var vendor = await CreateVendorAsync(md, "Filterable-Vendor");
        var uniqueTitle = $"Unique-Title-{Guid.NewGuid():N}"[..30];
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/catalog-sections",
            new CreateCatalogSectionRequest(vendor.Id, uniqueTitle, md.Categories.Single(c => c.Name == "Jewellery").Id, ["clearance-sale"]));
        var createdBody = await created.Content.ReadFromJsonAsync<CatalogSectionDto>();

        var bySearch = await _fixture.AssociateClient.GetAsync($"/api/v1/catalog-sections?search={Uri.EscapeDataString(uniqueTitle)}");
        var byVendor = await _fixture.AssociateClient.GetAsync($"/api/v1/catalog-sections?vendorId={vendor.Id}");
        var byTag = await _fixture.AssociateClient.GetAsync($"/api/v1/catalog-sections?tag=clearance-sale");

        (await bySearch.Content.ReadFromJsonAsync<CatalogSectionListResultDto>())!.Items.Should().ContainSingle(i => i.Id == createdBody!.Id);
        (await byVendor.Content.ReadFromJsonAsync<CatalogSectionListResultDto>())!.Items.Should().ContainSingle(i => i.Id == createdBody!.Id);
        (await byTag.Content.ReadFromJsonAsync<CatalogSectionListResultDto>())!.Items.Should().Contain(i => i.Id == createdBody!.Id);
    }

    // ---- Get / Update / Delete -----------------------------------------------------------

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/catalog-sections/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_ChangesTitleAndTags_Returns200()
    {
        var md = await GetMasterDataAsync();
        var vendor = await CreateVendorAsync(md, "Updatable-Vendor");
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/catalog-sections",
            new CreateCatalogSectionRequest(vendor.Id, "Old Title", md.Categories.Single(c => c.Name == "Jewellery").Id, null));
        var body = await created.Content.ReadFromJsonAsync<CatalogSectionDto>();

        var updateResponse = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/catalog-sections/{body!.Id}",
            new UpdateCatalogSectionRequest("New Title", md.Categories.Single(c => c.Name == "Furniture").Id, ["updated"]));

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<CatalogSectionDto>();
        updated!.Title.Should().Be("New Title");
        updated.Category.Name.Should().Be("Furniture");
    }

    [Fact]
    public async Task Delete_KnownSection_Returns204_ThenGetReturns404()
    {
        var md = await GetMasterDataAsync();
        var vendor = await CreateVendorAsync(md, "Deletable-Vendor");
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/catalog-sections",
            new CreateCatalogSectionRequest(vendor.Id, "To Delete", md.Categories.First().Id, null));
        var body = await created.Content.ReadFromJsonAsync<CatalogSectionDto>();

        var deleteResponse = await _fixture.AssociateClient.DeleteAsync($"/api/v1/catalog-sections/{body!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _fixture.AssociateClient.GetAsync($"/api/v1/catalog-sections/{body.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_UnknownId_Returns404()
    {
        var response = await _fixture.AssociateClient.DeleteAsync($"/api/v1/catalog-sections/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Upload / versioning (E6-02, E6-03, E6-06) ---------------------------------------

    [Fact]
    public async Task Upload_ValidPdf_Returns201_MarkedLatest()
    {
        var md = await GetMasterDataAsync();
        var vendor = await CreateVendorAsync(md, "Upload-Vendor");
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/catalog-sections",
            new CreateCatalogSectionRequest(vendor.Id, "Upload Section", md.Categories.First().Id, null));
        var section = await created.Content.ReadFromJsonAsync<CatalogSectionDto>();

        var response = await _fixture.AssociateClient.PostAsync($"/api/v1/catalog-sections/{section!.Id}/documents", UploadForm(versionLabel: "v1"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = await response.Content.ReadFromJsonAsync<CatalogDocumentDto>();
        doc!.IsLatest.Should().BeTrue();
        doc.OriginalFilename.Should().Be("catalog.pdf");
        doc.VersionLabel.Should().Be("v1");
    }

    [Fact]
    public async Task Upload_SecondVersion_DemotesFirst()
    {
        var md = await GetMasterDataAsync();
        var vendor = await CreateVendorAsync(md, "Version-Vendor");
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/catalog-sections",
            new CreateCatalogSectionRequest(vendor.Id, "Version Section", md.Categories.First().Id, null));
        var section = await created.Content.ReadFromJsonAsync<CatalogSectionDto>();

        var first = await _fixture.AssociateClient.PostAsync($"/api/v1/catalog-sections/{section!.Id}/documents", UploadForm("v1.pdf", versionLabel: "v1"));
        await first.EnsureSuccessOrThrowWithBodyAsync();
        var second = await _fixture.AssociateClient.PostAsync($"/api/v1/catalog-sections/{section.Id}/documents", UploadForm("v2.pdf", versionLabel: "v2"));
        await second.EnsureSuccessOrThrowWithBodyAsync();

        var sectionResponse = await _fixture.AssociateClient.GetAsync($"/api/v1/catalog-sections/{section.Id}");
        var reloaded = await sectionResponse.Content.ReadFromJsonAsync<CatalogSectionDto>();

        reloaded!.Documents.Should().HaveCount(2);
        reloaded.Documents.Single(d => d.OriginalFilename == "v1.pdf").IsLatest.Should().BeFalse();
        reloaded.Documents.Single(d => d.OriginalFilename == "v2.pdf").IsLatest.Should().BeTrue();
    }

    [Fact]
    public async Task Upload_NonPdfContentType_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var vendor = await CreateVendorAsync(md, "BadUpload-Vendor");
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/catalog-sections",
            new CreateCatalogSectionRequest(vendor.Id, "Bad Upload Section", md.Categories.First().Id, null));
        var section = await created.Content.ReadFromJsonAsync<CatalogSectionDto>();

        var response = await _fixture.AssociateClient.PostAsync($"/api/v1/catalog-sections/{section!.Id}/documents", UploadForm(contentType: "image/png"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Upload_UnknownSectionId_Returns404()
    {
        var response = await _fixture.AssociateClient.PostAsync($"/api/v1/catalog-sections/{Guid.NewGuid()}/documents", UploadForm());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- DoD: every permission-gated endpoint needs a 403-for-missing-permission test ---

    [Theory]
    [InlineData("GET", "/api/v1/catalog-sections")]
    [InlineData("GET", "/api/v1/catalog-sections/00000000-0000-0000-0000-000000000000")]
    public async Task NoPermissionCaller_Gets403_OnCatalogsViewGatedEndpoints(string method, string path)
    {
        var client = await GetNoPermissionClientAsync();

        var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("POST", "/api/v1/catalog-sections")]
    [InlineData("PUT", "/api/v1/catalog-sections/00000000-0000-0000-0000-000000000000")]
    [InlineData("DELETE", "/api/v1/catalog-sections/00000000-0000-0000-0000-000000000000")]
    public async Task NoPermissionCaller_Gets403_OnCatalogsEditGatedEndpoints(string method, string path)
    {
        var client = await GetNoPermissionClientAsync();

        var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnDocumentUpload()
    {
        var client = await GetNoPermissionClientAsync();

        var response = await client.PostAsync($"/api/v1/catalog-sections/{Guid.NewGuid()}/documents", UploadForm());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> GetNoPermissionClientAsync()
    {
        var auth = await AdminApiTestHelpers.ProvisionActiveUserAsync(
            _fixture.Factory, _fixture.AdminClient, roleIds: [], "No Perms Catalog User", Guid.NewGuid().ToString("N")[..8], "NoPerms-Changed-Pw1!");
        return _fixture.Factory.CreateClient().WithBearer(auth.AccessToken);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path) => method switch
    {
        "GET" => client.GetAsync(path),
        "POST" => client.PostAsync(path, new StringContent("{}", Encoding.UTF8, "application/json")),
        "PUT" => client.PutAsync(path, new StringContent("{}", Encoding.UTF8, "application/json")),
        "DELETE" => client.DeleteAsync(path),
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };
}
