using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.MasterData;
using SourcingOps.Application.Vendors;

namespace SourcingOps.Api.Tests.Vendors;

/// <summary>
/// Exercises ACTION_PLAN E5-01…E5-06 over real HTTP against the coordinator's binding
/// `/api/v1/vendors` contract, through <see cref="AdminSeededFixture"/>'s already-provisioned
/// Super Admin/Associate clients — real Postgres (Testcontainers), real JWT auth pipeline, real
/// permission policies.
/// </summary>
public class VendorsEndpointTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public VendorsEndpointTests(AdminSeededFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<MasterDataAggregateDto> GetMasterDataAsync()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/master-data");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<MasterDataAggregateDto>())!;
    }

    private static CreateVendorRequest ValidRequest(MasterDataAggregateDto md, string namePrefix) => new(
        Name: $"{namePrefix}-{Guid.NewGuid():N}",
        ContactPerson: "Li Wei",
        Phone: "+86-138-0000-0000",
        Email: "li.wei@example.com",
        Region: "Yiwu, Zhejiang",
        StatusId: md.VendorStatuses.Single(s => s.Code == "ACTIVE").Id,
        CategoryIds: [md.Categories.Single(c => c.Name == "Jewellery").Id],
        Moq: "500 units",
        LeadTime: "15-20 days",
        PaymentTerms: "30% deposit, 70% before shipment",
        ReliabilityRating: 4.5m,
        Notes: "Reliable long-term partner.");

    // ---- Create (E5-01…E5-03, E5-06) --------------------------------------------------

    [Fact]
    public async Task Create_ValidVendor_Returns201_WithEmbeddedStatusAndCategories()
    {
        var md = await GetMasterDataAsync();
        var request = ValidRequest(md, "Golden-Dragon");

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/vendors", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<VendorDetailDto>();
        body!.Status.Code.Should().Be("ACTIVE");
        body.Categories.Should().ContainSingle(c => c.Name == "Jewellery");
        body.PaymentTerms.Should().Be("30% deposit, 70% before shipment");
        body.CatalogSections.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_BlankName_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var request = ValidRequest(md, "Blank") with { Name = "   " };

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/vendors", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Create_UnknownStatusId_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var request = ValidRequest(md, "BadStatus") with { StatusId = Guid.NewGuid() };

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/vendors", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- List / search / filter (E5-04) ----------------------------------------------

    [Fact]
    public async Task List_CombinedFilters_ReturnEnvelopeShapeAndMatchingResults()
    {
        var md = await GetMasterDataAsync();
        var uniqueRegion = $"TestRegion-{Guid.NewGuid():N}"[..20];
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/vendors", ValidRequest(md, "Filterable") with { Region = uniqueRegion });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await created.Content.ReadFromJsonAsync<VendorDetailDto>();

        var categoryId = md.Categories.Single(c => c.Name == "Jewellery").Id;
        var statusId = md.VendorStatuses.Single(s => s.Code == "ACTIVE").Id;

        var response = await _fixture.AssociateClient.GetAsync(
            $"/api/v1/vendors?region={Uri.EscapeDataString(uniqueRegion)}&categoryId={categoryId}&statusId={statusId}&page=1&pageSize=25");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<VendorListResultDto>();
        result!.Items.Should().ContainSingle(i => i.Id == createdBody!.Id);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(25);
    }

    [Fact]
    public async Task List_SearchByName_FindsVendor()
    {
        var md = await GetMasterDataAsync();
        var uniqueName = $"Findable-Vendor-{Guid.NewGuid():N}"[..30];
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/vendors", ValidRequest(md, "x") with { Name = uniqueName });
        var createdBody = await created.Content.ReadFromJsonAsync<VendorDetailDto>();

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/vendors?search={Uri.EscapeDataString(uniqueName)}");

        var result = await response.Content.ReadFromJsonAsync<VendorListResultDto>();
        result!.Items.Should().ContainSingle(i => i.Id == createdBody!.Id);
    }

    // ---- Get / Update ------------------------------------------------------------------

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/vendors/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_ChangesStatusAndCategories_Returns200()
    {
        var md = await GetMasterDataAsync();
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/vendors", ValidRequest(md, "Updatable"));
        var body = await created.Content.ReadFromJsonAsync<VendorDetailDto>();
        var onHoldId = md.VendorStatuses.Single(s => s.Code == "ON-HOLD").Id;
        var furnitureId = md.Categories.Single(c => c.Name == "Furniture").Id;

        var updateRequest = new UpdateVendorRequest(
            body!.Name, body.ContactPerson, body.Phone, body.Email, body.Region,
            onHoldId, [furnitureId], body.Moq, body.LeadTime, body.PaymentTerms, body.ReliabilityRating, body.Notes);

        var updateResponse = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/vendors/{body.Id}", updateRequest);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<VendorDetailDto>();
        updated!.Status.Code.Should().Be("ON-HOLD");
        updated.Categories.Should().ContainSingle(c => c.Name == "Furniture");
    }

    [Fact]
    public async Task Update_UnknownId_Returns404()
    {
        var md = await GetMasterDataAsync();
        var request = ValidRequest(md, "Ghost");
        var updateRequest = new UpdateVendorRequest(
            request.Name, request.ContactPerson, request.Phone, request.Email, request.Region,
            request.StatusId, request.CategoryIds, request.Moq, request.LeadTime, request.PaymentTerms,
            request.ReliabilityRating, request.Notes);

        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/vendors/{Guid.NewGuid()}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- E5-05: vendor detail embeds catalog sections + documents (depends on E6) -------

    [Fact]
    public async Task Get_AfterCreatingCatalogSectionAndUploadingDocument_EmbedsBothOnDetail()
    {
        var md = await GetMasterDataAsync();
        var vendorResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/vendors", ValidRequest(md, "WithCatalog"));
        var vendor = await vendorResponse.Content.ReadFromJsonAsync<VendorDetailDto>();

        var sectionResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/catalog-sections", new
        {
            vendorId = vendor!.Id,
            title = "Spring 2026 Collection",
            categoryId = md.Categories.Single(c => c.Name == "Jewellery").Id,
            tags = new[] { "new arrivals" }
        });
        sectionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var section = await sectionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var sectionId = section.GetProperty("id").GetGuid();

        using var form = new MultipartFormDataContent();
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4 fake content for test");
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "catalog.pdf");
        var uploadResponse = await _fixture.AssociateClient.PostAsync($"/api/v1/catalog-sections/{sectionId}/documents", form);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var detailResponse = await _fixture.AssociateClient.GetAsync($"/api/v1/vendors/{vendor.Id}");
        var detail = await detailResponse.Content.ReadFromJsonAsync<VendorDetailDto>();

        detail!.CatalogCount.Should().Be(1);
        detail.CatalogSections.Should().ContainSingle(s => s.Id == sectionId);
        detail.CatalogSections.Single().Documents.Should().ContainSingle(d => d.OriginalFilename == "catalog.pdf");
    }

    // ---- DoD: every permission-gated endpoint needs a 403-for-missing-permission test ---

    [Theory]
    [InlineData("GET", "/api/v1/vendors")]
    [InlineData("GET", "/api/v1/vendors/00000000-0000-0000-0000-000000000000")]
    public async Task NoPermissionCaller_Gets403_OnVendorsViewGatedEndpoints(string method, string path)
    {
        var client = await GetNoPermissionClientAsync();

        var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("POST", "/api/v1/vendors")]
    [InlineData("PUT", "/api/v1/vendors/00000000-0000-0000-0000-000000000000")]
    public async Task NoPermissionCaller_Gets403_OnVendorsEditGatedEndpoints(string method, string path)
    {
        var client = await GetNoPermissionClientAsync();

        var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> GetNoPermissionClientAsync()
    {
        var auth = await AdminApiTestHelpers.ProvisionActiveUserAsync(
            _fixture.Factory, _fixture.AdminClient, roleIds: [], "No Perms Vendor User", Guid.NewGuid().ToString("N")[..8], "NoPerms-Changed-Pw1!");
        return _fixture.Factory.CreateClient().WithBearer(auth.AccessToken);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path) => method switch
    {
        "GET" => client.GetAsync(path),
        "POST" => client.PostAsync(path, new StringContent("{}", Encoding.UTF8, "application/json")),
        "PUT" => client.PutAsync(path, new StringContent("{}", Encoding.UTF8, "application/json")),
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };
}
