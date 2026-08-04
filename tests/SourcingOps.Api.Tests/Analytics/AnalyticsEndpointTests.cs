using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Catalog;
using SourcingOps.Application.Crm;
using SourcingOps.Application.Dispatching;
using SourcingOps.Application.MasterData;
using SourcingOps.Application.Vendors;

namespace SourcingOps.Api.Tests.Analytics;

/// <summary>
/// Exercises ACTION_PLAN E10-01…E10-07/E10-10 over real HTTP against a Testcontainers
/// Postgres, per the M7 contract. Weighted per the §7 DoD towards the hole named explicitly in
/// this pass's brief: a test that only round-trips through <c>ReadFromJsonAsync&lt;TDto&gt;()</c>
/// proves the VALUE, never the wire PROPERTY NAME — a camelCase mismatch would pass every such
/// test while the Angular client silently reads <c>undefined</c>. Every endpoint below is
/// therefore also asserted against the RAW JSON string before deserialising.
/// </summary>
public class AnalyticsEndpointTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public AnalyticsEndpointTests(AdminSeededFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<MasterDataAggregateDto> GetMasterDataAsync()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/master-data");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<MasterDataAggregateDto>())!;
    }

    private async Task<Guid> CreateCustomerAsync(MasterDataAggregateDto md, Guid serviceTypeId, Guid? categoryId = null, string? sourceChannel = null)
    {
        var request = new CreateCustomerRequest(
            Name: $"Analytics Test {Guid.NewGuid():N}"[..24],
            BusinessName: null,
            Phone: $"+9197{Random.Shared.NextInt64(10000000, 99999999)}",
            Email: null, City: "Surat", Region: "Gujarat", SourceChannel: sourceChannel,
            ServiceTypeId: serviceTypeId,
            StatusId: md.LeadStatuses.First().Id,
            CategoryIds: categoryId.HasValue ? [categoryId.Value] : null,
            OwnerUserId: null, Tags: null, Notes: null,
            ExternalMarketplace: null, ExternalOrderRef: null, ExternalSupplierName: null,
            ExternalOrderValue: null, ExternalOrderCurrency: null, ExternalOrderDate: null,
            ConfirmDuplicate: true);

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", request);
        await response.EnsureSuccessOrThrowWithBodyAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Creates a vendor, catalog section and document, then dispatches it to a fresh customer — enough to make one real CatalogDispatched row so byStaff/byKind have a non-empty row to assert field names against.</summary>
    private async Task SendOneCatalogDispatchAsync(MasterDataAggregateDto md)
    {
        var customerId = await CreateCustomerAsync(md, md.ServiceTypes.Single(s => s.Code == "CIF").Id);

        var vendorRequest = new CreateVendorRequest(
            Name: $"Analytics-Dispatch-Vendor-{Guid.NewGuid():N}", ContactPerson: null, Phone: null, Email: null,
            Region: "Yiwu, Zhejiang", StatusId: md.VendorStatuses.Single(s => s.Code == "ACTIVE").Id,
            CategoryIds: null, Moq: null, LeadTime: null, PaymentTerms: null, ReliabilityRating: null, Notes: null);
        var vendorResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/vendors", vendorRequest);
        await vendorResponse.EnsureSuccessOrThrowWithBodyAsync();
        var vendor = (await vendorResponse.Content.ReadFromJsonAsync<VendorDetailDto>())!;

        var sectionResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/catalog-sections",
            new CreateCatalogSectionRequest(vendor.Id, "Analytics Test Section", md.Categories.First().Id, null));
        await sectionResponse.EnsureSuccessOrThrowWithBodyAsync();
        var section = (await sectionResponse.Content.ReadFromJsonAsync<CatalogSectionDto>())!;

        using var form = new MultipartFormDataContent();
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4 analytics dispatch test content");
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "catalog.pdf");
        var uploadResponse = await _fixture.AssociateClient.PostAsync($"/api/v1/catalog-sections/{section.Id}/documents", form);
        await uploadResponse.EnsureSuccessOrThrowWithBodyAsync();
        var document = (await uploadResponse.Content.ReadFromJsonAsync<CatalogDocumentDto>())!;

        var dispatchResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/dispatch-log",
            new CreateDispatchLogRequest(customerId, document.Id, null, "Analytics wire-name test message"));
        await dispatchResponse.EnsureSuccessOrThrowWithBodyAsync();
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static string TodayRange => $"fromDate={Today:yyyy-MM-dd}&toDate={Today:yyyy-MM-dd}";

    // ---- Wire-name assertions (the mandatory hole-closer) ----------------------------------

    [Fact]
    public async Task Leads_RawJson_ContainsExpectedWireNames()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/analytics/leads");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        var raw = await response.Content.ReadAsStringAsync();

        foreach (var name in new[]
        {
            "\"series\"", "\"periodStart\"", "\"count\"",
            "\"bySource\"", "\"byStatus\"", "\"id\"", "\"code\"", "\"label\"", "\"sortOrder\"",
            "\"totalLeads\"", "\"wonCount\"", "\"conversionRate\"",
            "\"currentPeriodCount\"", "\"priorPeriodCount\""
        })
        {
            raw.Should().Contain(name, $"the Angular client reads this exact property name off /analytics/leads");
        }
    }

    [Fact]
    public async Task ServiceSplit_RawJson_ContainsExpectedWireNames()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/analytics/service-split");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        var raw = await response.Content.ReadAsStringAsync();

        foreach (var name in new[] { "\"totalCustomers\"", "\"items\"", "\"id\"", "\"code\"", "\"label\"", "\"sortOrder\"", "\"customerCount\"" })
        {
            raw.Should().Contain(name, "the Angular client reads this exact property name off /analytics/service-split");
        }
    }

    [Fact]
    public async Task CategoryMix_RawJson_ContainsExpectedWireNames()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/analytics/category-mix");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        var raw = await response.Content.ReadAsStringAsync();

        foreach (var name in new[] { "\"items\"", "\"id\"", "\"code\"", "\"label\"", "\"sortOrder\"", "\"customerCount\"" })
        {
            raw.Should().Contain(name, "the Angular client reads this exact property name off /analytics/category-mix");
        }
    }

    [Fact]
    public async Task Vendors_RawJson_ContainsExpectedWireNames()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/analytics/vendors");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        var raw = await response.Content.ReadAsStringAsync();

        foreach (var name in new[]
        {
            "\"totalActiveVendors\"", "\"byCategory\"", "\"id\"", "\"code\"", "\"label\"", "\"sortOrder\"",
            "\"vendorCount\"", "\"catalogCount\""
        })
        {
            raw.Should().Contain(name, "the Angular client reads this exact property name off /analytics/vendors");
        }
    }

    [Fact]
    public async Task Inventory_RawJson_ContainsExpectedWireNames()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/analytics/inventory");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        var raw = await response.Content.ReadAsStringAsync();

        foreach (var name in new[]
        {
            "\"onHandValue\"", "\"byCategory\"", "\"quantity\"", "\"value\"",
            "\"shipmentsByStatus\"", "\"inTransitCount\"", "\"pastEtaCount\"", "\"belowReorderCount\""
        })
        {
            raw.Should().Contain(name, "the Angular client reads this exact property name off /analytics/inventory");
        }
    }

    [Fact]
    public async Task Dispatch_RawJson_ContainsExpectedWireNames()
    {
        // byStaff needs at least one real dispatch to have a non-empty row to assert
        // userId/name against — byKind/series/totalDispatches are always present even at 0.
        // Uses TodayRange (not the bare path) so this call's cache key ("analytics:dispatch:
        // <today>:<today>:-:-") is distinct from AssociateCaller_Gets200's bare-path call
        // ("analytics:dispatch:-:-:-:-") — otherwise, whichever of the two runs first under
        // E10-10's 60s TTL would silently serve its (possibly dispatch-less) response to the
        // other, which is exactly the trap this test hit on first write.
        var md = await GetMasterDataAsync();
        await SendOneCatalogDispatchAsync(md);

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/analytics/dispatch?{TodayRange}");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        var raw = await response.Content.ReadAsStringAsync();

        foreach (var name in new[]
        {
            "\"series\"", "\"periodStart\"", "\"count\"",
            "\"byStaff\"", "\"userId\"", "\"name\"",
            "\"byKind\"", "\"kind\"", "\"totalDispatches\""
        })
        {
            raw.Should().Contain(name, "the Angular client reads this exact property name off /analytics/dispatch");
        }
    }

    // ---- AuthZ (E10 contract: gated on Analytics.View) -------------------------------------

    [Theory]
    [InlineData("/api/v1/analytics/leads")]
    [InlineData("/api/v1/analytics/service-split")]
    [InlineData("/api/v1/analytics/category-mix")]
    [InlineData("/api/v1/analytics/vendors")]
    [InlineData("/api/v1/analytics/inventory")]
    [InlineData("/api/v1/analytics/dispatch")]
    public async Task NoPermissionCaller_Gets403(string path)
    {
        var client = await GetNoPermissionClientAsync();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/v1/analytics/leads")]
    [InlineData("/api/v1/analytics/service-split")]
    [InlineData("/api/v1/analytics/category-mix")]
    [InlineData("/api/v1/analytics/vendors")]
    [InlineData("/api/v1/analytics/inventory")]
    [InlineData("/api/v1/analytics/dispatch")]
    public async Task AssociateCaller_Gets200(string path)
    {
        var response = await _fixture.AssociateClient.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpClient> GetNoPermissionClientAsync()
    {
        var auth = await AdminApiTestHelpers.ProvisionActiveUserAsync(
            _fixture.Factory, _fixture.AdminClient, roleIds: [], "No Perms Analytics User", Guid.NewGuid().ToString("N")[..8], "NoPerms-Changed-Pw1!");
        return _fixture.Factory.CreateClient().WithBearer(auth.AccessToken);
    }

    // ---- Validation --------------------------------------------------------------------

    [Fact]
    public async Task FromDateAfterToDate_Returns400ProblemDetails()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/analytics/leads?fromDate=2026-08-10&toDate=2026-08-01");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    // ---- Filters actually narrow results ----------------------------------------------

    /// <summary>
    /// NOTE ON TEST SHAPE: with E10-10 caching live (60s TTL keyed on route+filters), calling
    /// the SAME url twice ("before"/"after" the same filter) would legitimately be served the
    /// stale cached "before" value the second time — that is caching working as designed, not a
    /// bug, and an earlier draft of this test tripped over exactly that. So this proves
    /// narrowing with a single post-creation measurement per filter combo (each combo queried
    /// only once, so every call is a fresh, never-before-seen cache key) plus a unique
    /// <c>SourceChannel</c> marker to identify OUR row rather than relying on a total count that
    /// other tests in this shared-fixture class may also be contributing to.
    /// </summary>
    [Fact]
    public async Task Leads_ServiceTypeFilter_NarrowsCount_OverHttp()
    {
        var md = await GetMasterDataAsync();
        var cifId = md.ServiceTypes.Single(s => s.Code == "CIF").Id;
        var freightId = md.ServiceTypes.Single(s => s.Code == "FREIGHT_ONLY").Id;
        var marker = $"SVCTEST{Guid.NewGuid():N}".ToUpperInvariant();

        await CreateCustomerAsync(md, cifId, sourceChannel: marker);

        var cifResult = await GetLeadsAsync($"serviceTypeId={cifId}&{TodayRange}");
        var freightResult = await GetLeadsAsync($"serviceTypeId={freightId}&{TodayRange}");

        cifResult.BySource.Single(s => s.Code == marker).Count.Should().Be(1,
            "the CIF filter must include the lead we just created under that service type");
        freightResult.BySource.Single(s => s.Code == marker).Count.Should().Be(0,
            "the FreightOnly filter must exclude a lead created under CIF — proving the filter actually narrows");
    }

    [Fact]
    public async Task CategoryMix_CategoryFilter_NarrowsOverHttp()
    {
        var md = await GetMasterDataAsync();
        var jewellery = md.Categories.Single(c => c.Name == "Jewellery");
        var furniture = md.Categories.Single(c => c.Name == "Furniture");
        var cifId = md.ServiceTypes.Single(s => s.Code == "CIF").Id;

        await CreateCustomerAsync(md, cifId, categoryId: jewellery.Id);

        var filteredToJewellery = await GetCategoryMixAsync($"categoryId={jewellery.Id}&{TodayRange}");
        var filteredToFurniture = await GetCategoryMixAsync($"categoryId={furniture.Id}&{TodayRange}");

        var jewelleryUnderJewelleryFilter = filteredToJewellery.Items.Single(i => i.Label == "Jewellery").CustomerCount;
        var jewelleryUnderFurnitureFilter = filteredToFurniture.Items.Single(i => i.Label == "Jewellery").CustomerCount;

        jewelleryUnderJewelleryFilter.Should().BeGreaterThan(jewelleryUnderFurnitureFilter,
            "restricting the customer set to categoryId=Jewellery must include the customer we just tagged Jewellery, while restricting to Furniture must exclude it");
    }

    // ---- Zero-fill over the wire ---------------------------------------------------------

    [Fact]
    public async Task Leads_ByStatus_ZeroFillsOverTheWire_ForEveryConfiguredLeadStatus()
    {
        var md = await GetMasterDataAsync();

        var result = await GetLeadsAsync(null);

        result.ByStatus.Select(s => s.Code).Should().BeEquivalentTo(md.LeadStatuses.Select(s => s.Code),
            "every configured lead status must appear in the response, including ones with zero leads (E10 rule 1)");
        result.ByStatus.Should().OnlyContain(s => s.Count >= 0);
    }

    [Fact]
    public async Task CategoryMix_ZeroFillsOverTheWire_ForEveryConfiguredCategory()
    {
        var md = await GetMasterDataAsync();

        var result = await GetCategoryMixAsync(null);

        result.Items.Select(i => i.Label).Should().BeEquivalentTo(md.Categories.Select(c => c.Name),
            "every configured category must appear in the response, including ones with zero customers (E10 rule 1)");
    }

    // ---- HTTP helpers -----------------------------------------------------------------

    private async Task<LeadsAnalyticsResponse> GetLeadsAsync(string? query)
    {
        var url = "/api/v1/analytics/leads" + (query is null ? "" : $"?{query}");
        var response = await _fixture.AssociateClient.GetAsync(url);
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<LeadsAnalyticsResponse>())!;
    }

    private async Task<CategoryMixAnalyticsResponse> GetCategoryMixAsync(string? query)
    {
        var url = "/api/v1/analytics/category-mix" + (query is null ? "" : $"?{query}");
        var response = await _fixture.AssociateClient.GetAsync(url);
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<CategoryMixAnalyticsResponse>())!;
    }

    // Local deserialisation shapes, deliberately independent of SourcingOps.Application.Analytics'
    // own DTOs — this suite must prove the WIRE contract, not merely that the same C# type
    // round-trips through the same serializer on both ends (see class doc comment).
    private sealed record LookupCountWire(Guid Id, string Code, string Label, int SortOrder, int Count);
    private sealed record LookupCustomerCountWire(Guid Id, string Code, string Label, int SortOrder, int CustomerCount);
    private sealed record LeadsAnalyticsResponse(int TotalLeads, int WonCount, decimal ConversionRate, IReadOnlyList<LookupCountWire> ByStatus, IReadOnlyList<LookupCountWire> BySource);
    private sealed record CategoryMixAnalyticsResponse(IReadOnlyList<LookupCustomerCountWire> Items);
}
