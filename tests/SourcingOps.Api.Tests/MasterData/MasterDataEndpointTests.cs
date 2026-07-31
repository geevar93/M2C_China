using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.MasterData;
using SourcingOps.Domain.Entities;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Api.Tests.MasterData;

/// <summary>
/// Covers ACTION_PLAN E3-03…E3-09 over real HTTP against a Testcontainers Postgres instance
/// (TECH_SPEC §4.1). Also exercises half of N-2 (ACTION_PLAN §9.2): an Associate lacking
/// Admin.ManageMasterData gets 403 on a write — see <see cref="Authorization.PermissionDeniedTests"/>
/// for the full N-2 closure (both the permission-403 and must-change-password-403 paths).
/// </summary>
public class MasterDataEndpointTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public MasterDataEndpointTests(AdminSeededFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetAggregate_AnyAuthenticatedUser_ReturnsSeededDataWithContractShapes()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/master-data?includeRetired=false");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MasterDataAggregateDto>();

        body!.Categories.Should().Contain(c => c.Name == "Jewellery");
        body.ServiceTypes.Should().Contain(s => s.Code == "CIF" && s.Label == "CIF");
        body.ShipmentStatuses.Should().Contain(s => s.Code == "IN TRANSIT");
        body.VendorStatuses.Should().Contain(s => s.Code == "ON-HOLD");
    }

    /// <summary>
    /// ACTION_PLAN N-12 audit. The client's `DOCUMENT_SCOPE_VENDOR`/`DOCUMENT_SCOPE_SHIPMENT`
    /// constants, the admin scope selector (N-20(b)) and both filtered upload dropdowns
    /// (N-20(c)) all compare against these exact strings — but every test on that side is a
    /// client test against mocked HTTP, so **nothing pinned what the API actually serialises**.
    /// A casing or spelling drift here would silently empty a dropdown rather than fail loudly,
    /// which is the failure mode N-20(c) exists to prevent.
    ///
    /// Literals on purpose: reading them back from <c>DocumentTypeScopes</c> would pass no
    /// matter what that class says, which is the D-50 defect class exactly.
    /// </summary>
    [Fact]
    public async Task GetAggregate_SerialisesDocumentTypeScopes_AsTheLiteralStringsTheClientMatchesOn()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/master-data?includeRetired=false");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MasterDataAggregateDto>();

        body!.DocumentTypes.Should().NotBeEmpty();
        body.DocumentTypes.Select(d => d.Scope).Distinct().Should().BeSubsetOf(new[] { "Vendor", "Shipment" });

        // Both scopes must actually be present, or a "filtered to Shipment" dropdown could be
        // empty for a reason no test would catch (D-34 seeds four shipment-scoped defaults).
        body.DocumentTypes.Should().Contain(d => d.Scope == "Vendor");
        body.DocumentTypes.Should().Contain(d => d.Scope == "Shipment");

        // Trailing-nullable contract (D-45): every other LookupItemDto collection carries the
        // field as null rather than omitting it, which is what keeps MasterDataService generic.
        body.ServiceTypes.Should().OnlyContain(s => s.Scope == null);
        body.ShipmentStatuses.Should().OnlyContain(s => s.Scope == null);
    }

    [Fact]
    public async Task GetAggregate_WithoutAuthentication_Returns401()
    {
        var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/v1/master-data");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateCategory_AsAdmin_ReturnsCreated_AndAppearsInAggregate()
    {
        var name = $"E2E-Cat-{Guid.NewGuid():N}"[..24];

        var createResponse = await _fixture.AdminClient.PostAsJsonAsync("/api/v1/master-data/categories",
            new UpsertMasterDataRequest(name, null, null));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CategoryDto>();
        created!.Name.Should().Be(name);

        var aggregateResponse = await _fixture.AdminClient.GetAsync("/api/v1/master-data?includeRetired=false");
        var aggregate = await aggregateResponse.Content.ReadFromJsonAsync<MasterDataAggregateDto>();
        aggregate!.Categories.Should().Contain(c => c.Id == created.Id);
    }

    [Fact]
    public async Task CreateCategory_AsAssociate_Returns403()
    {
        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/master-data/categories",
            new UpsertMasterDataRequest("Should Not Be Created", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RetireThenRestore_Category_ControlsVisibilityByIncludeRetired()
    {
        var name = $"E2E-Retire-{Guid.NewGuid():N}"[..24];
        var createResponse = await _fixture.AdminClient.PostAsJsonAsync("/api/v1/master-data/categories",
            new UpsertMasterDataRequest(name, null, null));
        var created = (await createResponse.Content.ReadFromJsonAsync<CategoryDto>())!;

        var retireResponse = await _fixture.AdminClient.PostAsync($"/api/v1/master-data/categories/{created.Id}/retire", content: null);
        retireResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var activeOnly = await (await _fixture.AdminClient.GetAsync("/api/v1/master-data?includeRetired=false"))
            .Content.ReadFromJsonAsync<MasterDataAggregateDto>();
        activeOnly!.Categories.Should().NotContain(c => c.Id == created.Id);

        var everything = await (await _fixture.AdminClient.GetAsync("/api/v1/master-data?includeRetired=true"))
            .Content.ReadFromJsonAsync<MasterDataAggregateDto>();
        everything!.Categories.Should().Contain(c => c.Id == created.Id && !c.IsActive);

        var restoreResponse = await _fixture.AdminClient.PostAsync($"/api/v1/master-data/categories/{created.Id}/restore", content: null);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterRestore = await (await _fixture.AdminClient.GetAsync("/api/v1/master-data?includeRetired=false"))
            .Content.ReadFromJsonAsync<MasterDataAggregateDto>();
        afterRestore!.Categories.Should().Contain(c => c.Id == created.Id && c.IsActive);
    }

    [Fact]
    public async Task UpdateLookup_EditsLabelOnly_CodeUnchanged()
    {
        var code = $"E2E-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var createResponse = await _fixture.AdminClient.PostAsJsonAsync("/api/v1/master-data/shipment-statuses",
            new UpsertMasterDataRequest(null, code, "Original Label"));
        var created = (await createResponse.Content.ReadFromJsonAsync<LookupItemDto>())!;

        var updateResponse = await _fixture.AdminClient.PutAsJsonAsync($"/api/v1/master-data/shipment-statuses/{created.Id}",
            new UpsertMasterDataRequest(null, "IGNORED", "Updated Label"));

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<LookupItemDto>();
        updated!.Label.Should().Be("Updated Label");
        updated.Code.Should().Be(code);
    }

    [Fact]
    public async Task Reorder_AppliesNewSortOrders()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var aResponse = await _fixture.AdminClient.PostAsJsonAsync("/api/v1/master-data/invoice-statuses",
            new UpsertMasterDataRequest(null, $"A-{suffix}", "A"));
        var bResponse = await _fixture.AdminClient.PostAsJsonAsync("/api/v1/master-data/invoice-statuses",
            new UpsertMasterDataRequest(null, $"B-{suffix}", "B"));
        var a = (await aResponse.Content.ReadFromJsonAsync<LookupItemDto>())!;
        var b = (await bResponse.Content.ReadFromJsonAsync<LookupItemDto>())!;

        var reorderResponse = await _fixture.AdminClient.PutAsJsonAsync("/api/v1/master-data/invoice-statuses/reorder",
            new[] { new ReorderItemDto(b.Id, a.SortOrder), new ReorderItemDto(a.Id, b.SortOrder) });

        reorderResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var everything = await (await _fixture.AdminClient.GetAsync("/api/v1/master-data?includeRetired=true"))
            .Content.ReadFromJsonAsync<MasterDataAggregateDto>();
        everything!.InvoiceStatuses.Single(x => x.Id == a.Id).SortOrder.Should().Be(b.SortOrder);
        everything.InvoiceStatuses.Single(x => x.Id == b.Id).SortOrder.Should().Be(a.SortOrder);
    }

    [Fact]
    public async Task DeleteUnreferencedRow_Returns204()
    {
        // Created via the API, not the seeder — IsSystemDefault is false, so this is also
        // N-8's "user-added custom rows keep working exactly as built" half.
        var code = $"DEL-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var createResponse = await _fixture.AdminClient.PostAsJsonAsync("/api/v1/master-data/lead-statuses",
            new UpsertMasterDataRequest(null, code, "Deletable"));
        var created = (await createResponse.Content.ReadFromJsonAsync<LookupItemDto>())!;
        created.IsSystemDefault.Should().BeFalse();

        var deleteResponse = await _fixture.AdminClient.DeleteAsync($"/api/v1/master-data/lead-statuses/{created.Id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteReferencedRow_Returns409_WithRetireGuidance()
    {
        var code = $"REF-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var createResponse = await _fixture.AdminClient.PostAsJsonAsync("/api/v1/master-data/vendor-statuses",
            new UpsertMasterDataRequest(null, code, "Referenced Status"));
        var created = (await createResponse.Content.ReadFromJsonAsync<LookupItemDto>())!;

        // No VendorsController exists yet in M2 (M3+ scope) — insert the referencing row
        // directly against the same Testcontainers database the running API uses, exactly
        // as a future milestone's VendorsController would.
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Vendors.Add(new Vendor { Id = Guid.NewGuid(), Name = $"Vendor-{code}", StatusId = created.Id, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var deleteResponse = await _fixture.AdminClient.DeleteAsync($"/api/v1/master-data/vendor-statuses/{created.Id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        deleteResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await deleteResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("Retire");
    }

    [Fact]
    public async Task DeleteSeededActiveVendorStatus_Returns409_EvenThoughUnreferenced()
    {
        // ACTION_PLAN §10.2 N-8's exact reproduction: this is the specific row the M2
        // verification pass hard-deleted (nothing referenced it yet, so E3-08's original
        // logic let the delete through). Proves it is now retire-only.
        var aggregate = await (await _fixture.AdminClient.GetAsync("/api/v1/master-data?includeRetired=true"))
            .Content.ReadFromJsonAsync<MasterDataAggregateDto>();
        var activeVendorStatus = aggregate!.VendorStatuses.Single(s => s.Code == "ACTIVE");
        activeVendorStatus.IsSystemDefault.Should().BeTrue("the seeded row must carry the flag DbSeeder sets");

        var deleteResponse = await _fixture.AdminClient.DeleteAsync($"/api/v1/master-data/vendor-statuses/{activeVendorStatus.Id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        deleteResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await deleteResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("Retire");

        // Retire remains available and IS the intended action for this exact row.
        var retireResponse = await _fixture.AdminClient.PostAsync($"/api/v1/master-data/vendor-statuses/{activeVendorStatus.Id}/retire", content: null);
        retireResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Restore it so this test does not leave a poisoned seeded default behind for any
        // sibling test sharing the same class fixture / Testcontainers database.
        var restoreResponse = await _fixture.AdminClient.PostAsync($"/api/v1/master-data/vendor-statuses/{activeVendorStatus.Id}/restore", content: null);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteUnknownId_Returns404()
    {
        var response = await _fixture.AdminClient.DeleteAsync($"/api/v1/master-data/categories/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnknownCollectionSegment_Returns404()
    {
        var response = await _fixture.AdminClient.PostAsJsonAsync("/api/v1/master-data/not-a-real-collection",
            new UpsertMasterDataRequest("x", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task E3_09_CacheInvalidatesImmediatelyOnWrite_OverHttp()
    {
        var beforeCount = (await (await _fixture.AdminClient.GetAsync("/api/v1/master-data?includeRetired=false"))
            .Content.ReadFromJsonAsync<MasterDataAggregateDto>())!.Categories.Count;

        var name = $"E2E-CacheProof-{Guid.NewGuid():N}"[..28];
        var createResponse = await _fixture.AdminClient.PostAsJsonAsync("/api/v1/master-data/categories",
            new UpsertMasterDataRequest(name, null, null));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var afterResponse = await _fixture.AdminClient.GetAsync("/api/v1/master-data?includeRetired=false");
        var after = await afterResponse.Content.ReadFromJsonAsync<MasterDataAggregateDto>();

        after!.Categories.Should().HaveCount(beforeCount + 1, "the write must invalidate the cached aggregate, not require a TTL to expire");
        after.Categories.Should().Contain(c => c.Name == name);
    }
}
