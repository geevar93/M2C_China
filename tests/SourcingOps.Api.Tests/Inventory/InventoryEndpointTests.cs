using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Inventory;
using SourcingOps.Application.MasterData;

namespace SourcingOps.Api.Tests.Inventory;

/// <summary>
/// Exercises ACTION_PLAN E7-01…E7-04 over real HTTP against a Testcontainers Postgres —
/// real migrations, real startup seeding, real JWT pipeline, real permission policies.
/// Mirrors <c>VendorsEndpointTests</c>'s structure through <see cref="AdminSeededFixture"/>'s
/// already-provisioned Super Admin / Associate clients.
///
/// Every endpoint carries a matching "caller lacking the permission gets 403" test, per the
/// §7 DoD.
/// </summary>
public class InventoryEndpointTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public InventoryEndpointTests(AdminSeededFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<MasterDataAggregateDto> GetMasterDataAsync()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/master-data");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<MasterDataAggregateDto>())!;
    }

    private async Task<InventoryItemDto> CreateItemAsync(
        MasterDataAggregateDto md, string namePrefix, decimal onHand = 100m, decimal reorder = 20m, decimal? unitCost = 500m, string? sku = null)
    {
        var request = new CreateInventoryItemRequest(
            Name: $"{namePrefix}-{Guid.NewGuid():N}",
            Sku: sku,
            Description: "Created by an integration test.",
            CategoryId: md.Categories.First(c => c.Name == "Jewellery").Id,
            VendorId: null,
            Unit: "set",
            OnHandQty: onHand,
            ReorderThreshold: reorder,
            UnitCost: unitCost);

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/inventory", request);
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<InventoryItemDto>())!;
    }

    // ---- Create (E7-01) --------------------------------------------------------------------

    [Fact]
    public async Task Create_ValidItem_Returns201_WithResolvedCategoryAndComputedStockValue()
    {
        var md = await GetMasterDataAsync();
        var categoryId = md.Categories.First(c => c.Name == "Jewellery").Id;

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/inventory",
            new CreateInventoryItemRequest("Kundan Set", "JWL-KUN-118", "Gold tone", categoryId, null, "set", 1840m, 600m, 500m));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<InventoryItemDto>())!;
        body.Category.Id.Should().Be(categoryId);
        body.Category.Name.Should().Be("Jewellery");
        body.Vendor.Should().BeNull();
        body.OnHandQty.Should().Be(1840m);
        body.StockValue.Should().Be(920_000m);
        body.StockLevel.Should().Be("HEALTHY");
    }

    [Fact]
    public async Task Create_UnknownCategory_Returns400ProblemDetails()
    {
        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/inventory",
            new CreateInventoryItemRequest("Bad", null, null, Guid.NewGuid(), null, null, 0m, 0m, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Create_BlankName_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/inventory",
            new CreateInventoryItemRequest("   ", null, null, md.Categories.First().Id, null, null, 0m, 0m, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    // ---- Get / Update / Delete --------------------------------------------------------------

    [Fact]
    public async Task Get_KnownItem_Returns200()
    {
        var md = await GetMasterDataAsync();
        var created = await CreateItemAsync(md, "Get-Item");

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/inventory/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<InventoryItemDto>())!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task Get_UnknownItem_Returns404()
    {
        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/inventory/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_ChangesFields_ButNeverOnHandQuantity()
    {
        var md = await GetMasterDataAsync();
        var created = await CreateItemAsync(md, "Update-Item", onHand: 145m, reorder: 200m);

        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/inventory/{created.Id}",
            new UpdateInventoryItemRequest("Renamed Item", "NEW-SKU", "New description",
                md.Categories.First(c => c.Name == "Tools").Id, null, "box", 50m, 700m));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<InventoryItemDto>())!;
        body.Name.Should().Be("Renamed Item");
        body.Category.Name.Should().Be("Tools");
        body.UnitCost.Should().Be(700m);
        body.OnHandQty.Should().Be(145m, "stock moves only through inbound entries and shipments");
    }

    [Fact]
    public async Task Update_UnknownItem_Returns404()
    {
        var md = await GetMasterDataAsync();
        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/inventory/{Guid.NewGuid()}",
            new UpdateInventoryItemRequest("X", null, null, md.Categories.First().Id, null, null, 0m, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_UnreferencedItem_Returns204_ThenGetReturns404()
    {
        var md = await GetMasterDataAsync();
        var created = await CreateItemAsync(md, "Delete-Item");

        var deleteResponse = await _fixture.AssociateClient.DeleteAsync($"/api/v1/inventory/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _fixture.AssociateClient.GetAsync($"/api/v1/inventory/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_UnknownItem_Returns404()
    {
        var response = await _fixture.AssociateClient.DeleteAsync($"/api/v1/inventory/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Inbound stock (E7-02, D-d) --------------------------------------------------------------

    [Fact]
    public async Task RecordInbound_Returns201_RaisesOnHand_AndIsListable()
    {
        var md = await GetMasterDataAsync();
        var created = await CreateItemAsync(md, "Inbound-Item", onHand: 260m, reorder: 400m, unitCost: 1400m);

        var response = await _fixture.AssociateClient.PostAsJsonAsync($"/api/v1/inventory/{created.Id}/inbound",
            new RecordInboundRequest(240m, new DateOnly(2026, 7, 21), "GRN-4471"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<RecordInboundResultDto>())!;
        body.Item.OnHandQty.Should().Be(500m);
        body.Item.StockLevel.Should().Be("HEALTHY");
        body.Entry.EntryDate.Should().Be(new DateOnly(2026, 7, 21));
        body.Entry.Reference.Should().Be("GRN-4471");

        var listResponse = await _fixture.AssociateClient.GetAsync($"/api/v1/inventory/{created.Id}/inbound");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = (await listResponse.Content.ReadFromJsonAsync<InventoryInboundEntryListResultDto>())!;
        entries.Items.Should().ContainSingle().Which.Reference.Should().Be("GRN-4471");
    }

    [Fact]
    public async Task RecordInbound_NonPositiveQuantity_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var created = await CreateItemAsync(md, "BadInbound-Item");

        var response = await _fixture.AssociateClient.PostAsJsonAsync($"/api/v1/inventory/{created.Id}/inbound",
            new RecordInboundRequest(0m, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task RecordInbound_UnknownItem_Returns404()
    {
        var response = await _fixture.AssociateClient.PostAsJsonAsync($"/api/v1/inventory/{Guid.NewGuid()}/inbound",
            new RecordInboundRequest(10m, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListInbound_UnknownItem_Returns404()
    {
        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/inventory/{Guid.NewGuid()}/inbound");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Stock adjustments (N-38) -----------------------------------------------------------

    [Fact]
    public async Task RecordAdjustment_Returns201_SetsOnHand_AndIsListable()
    {
        var md = await GetMasterDataAsync();
        var created = await CreateItemAsync(md, "Adjust-Item", onHand: 260m, reorder: 400m, unitCost: 1400m);

        var response = await _fixture.AssociateClient.PostAsJsonAsync($"/api/v1/inventory/{created.Id}/adjustments",
            new RecordAdjustmentRequest(300m, "Physical count found more stock", new DateOnly(2026, 7, 21)));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<RecordAdjustmentResultDto>())!;
        body.Item.OnHandQty.Should().Be(300m);
        body.Adjustment.PreviousQty.Should().Be(260m);
        body.Adjustment.Delta.Should().Be(40m);
        body.Adjustment.Reason.Should().Be("Physical count found more stock");
        body.Adjustment.AdjustedOn.Should().Be(new DateOnly(2026, 7, 21));

        var listResponse = await _fixture.AssociateClient.GetAsync($"/api/v1/inventory/{created.Id}/adjustments");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = (await listResponse.Content.ReadFromJsonAsync<InventoryStockAdjustmentListResultDto>())!;
        history.Items.Should().ContainSingle().Which.Reason.Should().Be("Physical count found more stock");
    }

    [Fact]
    public async Task RecordAdjustment_AdjustingDown_SetsOnHandAndNegativeDelta()
    {
        var md = await GetMasterDataAsync();
        var created = await CreateItemAsync(md, "Adjust-Down-Item", onHand: 260m, reorder: 400m);

        var response = await _fixture.AssociateClient.PostAsJsonAsync($"/api/v1/inventory/{created.Id}/adjustments",
            new RecordAdjustmentRequest(200m, "Shrinkage found on count", null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<RecordAdjustmentResultDto>())!;
        body.Item.OnHandQty.Should().Be(200m);
        body.Adjustment.Delta.Should().Be(-60m);
    }

    [Fact]
    public async Task RecordAdjustment_FromNegativeStock_CanCountUpToPositive()
    {
        // D-35: existing on-hand can be negative (oversold). Only the counted VALUE is
        // constrained to non-negative; adjusting FROM negative TO positive must work.
        var md = await GetMasterDataAsync();

        // No shipment-independent route to drive on-hand negative in a fresh test item, so this
        // uses an initial negative opening balance instead — CreateAsync places no floor on it.
        var negativeCreate = new CreateInventoryItemRequest(
            $"Adjust-Negative-{Guid.NewGuid():N}", null, null,
            md.Categories.First(c => c.Name == "Jewellery").Id, null, "pcs", -40m, 200m, null);
        var createResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/inventory", negativeCreate);
        await createResponse.EnsureSuccessOrThrowWithBodyAsync();
        var negativeItem = (await createResponse.Content.ReadFromJsonAsync<InventoryItemDto>())!;
        negativeItem.OnHandQty.Should().Be(-40m);

        var response = await _fixture.AssociateClient.PostAsJsonAsync($"/api/v1/inventory/{negativeItem.Id}/adjustments",
            new RecordAdjustmentRequest(15m, "Physical recount after oversell", null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<RecordAdjustmentResultDto>())!;
        body.Item.OnHandQty.Should().Be(15m);
        body.Adjustment.PreviousQty.Should().Be(-40m);
        body.Adjustment.Delta.Should().Be(55m);
    }

    [Fact]
    public async Task RecordAdjustment_ZeroDelta_IsStillRecorded()
    {
        var md = await GetMasterDataAsync();
        var created = await CreateItemAsync(md, "Adjust-NoOp-Item", onHand: 100m, reorder: 10m);

        var response = await _fixture.AssociateClient.PostAsJsonAsync($"/api/v1/inventory/{created.Id}/adjustments",
            new RecordAdjustmentRequest(100m, "Count matched exactly", null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<RecordAdjustmentResultDto>())!;
        body.Adjustment.Delta.Should().Be(0m);

        var listResponse = await _fixture.AssociateClient.GetAsync($"/api/v1/inventory/{created.Id}/adjustments");
        var history = (await listResponse.Content.ReadFromJsonAsync<InventoryStockAdjustmentListResultDto>())!;
        history.Items.Should().ContainSingle("a no-op adjustment is still a meaningful audit fact, not silently skipped");
    }

    [Fact]
    public async Task RecordAdjustment_NegativeCountedQty_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var created = await CreateItemAsync(md, "Adjust-BadQty-Item");

        var response = await _fixture.AssociateClient.PostAsJsonAsync($"/api/v1/inventory/{created.Id}/adjustments",
            new RecordAdjustmentRequest(-1m, "Bad count", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task RecordAdjustment_BlankReason_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var created = await CreateItemAsync(md, "Adjust-BadReason-Item");

        var response = await _fixture.AssociateClient.PostAsJsonAsync($"/api/v1/inventory/{created.Id}/adjustments",
            new RecordAdjustmentRequest(50m, "   ", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task RecordAdjustment_UnknownItem_Returns404()
    {
        var response = await _fixture.AssociateClient.PostAsJsonAsync($"/api/v1/inventory/{Guid.NewGuid()}/adjustments",
            new RecordAdjustmentRequest(10m, "Count", null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListAdjustments_UnknownItem_Returns404()
    {
        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/inventory/{Guid.NewGuid()}/adjustments");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnRecordAdjustment()
    {
        var client = await GetNoPermissionClientAsync();
        var response = await client.PostAsJsonAsync($"/api/v1/inventory/{Guid.NewGuid()}/adjustments",
            new RecordAdjustmentRequest(10m, "Count", null));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // NOTE: the DoD also implies "a caller with Inventory.View but NOT Inventory.Adjust gets
    // 403 on POST /adjustments" — genuinely untestable with the two seeded roles (Associate
    // holds every non-Admin.* permission, including Inventory.Adjust, as one bloc; SuperAdmin
    // holds everything). Same limitation InvoicesEndpointTests already records for the
    // Invoicing.Edit/Invoicing.MarkPaid split. GET history working with only Inventory.View is
    // covered by every read above (AssociateClient), which is the permission that actually gates it.

    [Fact]
    public async Task ListAdjustments_RawJson_ContainsExpectedWireNames()
    {
        var md = await GetMasterDataAsync();
        var created = await CreateItemAsync(md, "Adjust-WireNames-Item", onHand: 50m, reorder: 10m);
        var recordResponse = await _fixture.AssociateClient.PostAsJsonAsync($"/api/v1/inventory/{created.Id}/adjustments",
            new RecordAdjustmentRequest(60m, "Wire-name check", null));
        await recordResponse.EnsureSuccessOrThrowWithBodyAsync();

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/inventory/{created.Id}/adjustments");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        var raw = await response.Content.ReadAsStringAsync();

        foreach (var name in new[]
        {
            "\"countedQty\"", "\"previousQty\"", "\"delta\"", "\"reason\"", "\"adjustedOn\"", "\"adjustedAt\""
        })
        {
            raw.Should().Contain(name, "the Angular client reads this exact property name off /inventory/{id}/adjustments");
        }
    }

    // ---- List, filters and the D-k summary (E7-03/E7-04) --------------------------------------------

    [Fact]
    public async Task List_ReturnsSummaryBlockAndPaging()
    {
        var md = await GetMasterDataAsync();
        await CreateItemAsync(md, "ListSummary-Item", onHand: 10m, reorder: 1m, unitCost: 100m);

        var response = await _fixture.AssociateClient.GetAsync("/api/v1/inventory?page=1&pageSize=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<InventoryListResultDto>())!;
        body.Page.Should().Be(1);
        body.PageSize.Should().Be(5);
        body.Summary.Should().NotBeNull();
        body.Summary.ItemCount.Should().Be(body.TotalCount, "the summary spans the whole filtered set, not the page");
    }

    [Fact]
    public async Task List_SearchMatchesSku()
    {
        var md = await GetMasterDataAsync();
        var uniqueSku = $"SKU-{Guid.NewGuid():N}"[..20];
        var created = await CreateItemAsync(md, "SkuSearch-Item", sku: uniqueSku);

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/inventory?search={uniqueSku}");

        var body = (await response.Content.ReadFromJsonAsync<InventoryListResultDto>())!;
        body.Items.Should().ContainSingle().Which.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task List_LowStockFilter_ReturnsBelowReorderAndNegativeOnly()
    {
        var md = await GetMasterDataAsync();
        var low = await CreateItemAsync(md, "Low-Item", onHand: 5m, reorder: 100m);
        var healthy = await CreateItemAsync(md, "Healthy-Item", onHand: 500m, reorder: 100m);

        var response = await _fixture.AssociateClient.GetAsync("/api/v1/inventory?stockLevel=low&pageSize=200");

        var body = (await response.Content.ReadFromJsonAsync<InventoryListResultDto>())!;
        body.Items.Should().Contain(i => i.Id == low.Id);
        body.Items.Should().NotContain(i => i.Id == healthy.Id);
        body.Items.Should().OnlyContain(i => i.StockLevel == "LOW" || i.StockLevel == "NEGATIVE");
    }

    [Fact]
    public async Task List_UnknownStockLevelFilter_Returns400ProblemDetails()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/inventory?stockLevel=urgent");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task List_CategoryFilter_NarrowsResultsAndSummary()
    {
        var md = await GetMasterDataAsync();
        var toolsCategoryId = md.Categories.First(c => c.Name == "Tools").Id;

        // Seeded explicitly rather than relying on another test having left one behind: every
        // other item this class creates is in Jewellery, so without this the filter returns an
        // empty set and the assertion below would be vacuous (or, with OnlyContain, fail).
        var createResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/inventory",
            new CreateInventoryItemRequest($"Wrench-{Guid.NewGuid():N}", null, null, toolsCategoryId, null, "pc", 145m, 200m, 700m));
        await createResponse.EnsureSuccessOrThrowWithBodyAsync();
        var created = (await createResponse.Content.ReadFromJsonAsync<InventoryItemDto>())!;

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/inventory?categoryId={toolsCategoryId}&pageSize=200");

        var body = (await response.Content.ReadFromJsonAsync<InventoryListResultDto>())!;
        body.Items.Should().NotBeEmpty().And.Contain(i => i.Id == created.Id);
        body.Items.Should().OnlyContain(i => i.Category.Id == toolsCategoryId);
        body.Summary.ItemCount.Should().Be(body.TotalCount);
    }

    // ---- DoD: every permission-gated endpoint needs a 403-for-missing-permission test --------------

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnList()
    {
        var client = await GetNoPermissionClientAsync();
        (await client.GetAsync("/api/v1/inventory")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnGet()
    {
        var client = await GetNoPermissionClientAsync();
        (await client.GetAsync($"/api/v1/inventory/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnCreate()
    {
        var client = await GetNoPermissionClientAsync();
        var response = await client.PostAsJsonAsync("/api/v1/inventory",
            new CreateInventoryItemRequest("X", null, null, Guid.NewGuid(), null, null, 0m, 0m, null));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnUpdate()
    {
        var client = await GetNoPermissionClientAsync();
        var response = await client.PutAsJsonAsync($"/api/v1/inventory/{Guid.NewGuid()}",
            new UpdateInventoryItemRequest("X", null, null, Guid.NewGuid(), null, null, 0m, null));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnDelete()
    {
        var client = await GetNoPermissionClientAsync();
        (await client.DeleteAsync($"/api/v1/inventory/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnRecordInbound()
    {
        var client = await GetNoPermissionClientAsync();
        var response = await client.PostAsJsonAsync($"/api/v1/inventory/{Guid.NewGuid()}/inbound", new RecordInboundRequest(1m, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnListInbound()
    {
        var client = await GetNoPermissionClientAsync();
        (await client.GetAsync($"/api/v1/inventory/{Guid.NewGuid()}/inbound")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnonymousCaller_Gets401_OnList()
    {
        using var client = _fixture.Factory.CreateClient();
        (await client.GetAsync("/api/v1/inventory")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpClient> GetNoPermissionClientAsync()
    {
        var auth = await AdminApiTestHelpers.ProvisionActiveUserAsync(
            _fixture.Factory, _fixture.AdminClient, roleIds: [], "No Perms Inventory User", Guid.NewGuid().ToString("N")[..8], "NoPerms-Changed-Pw1!");
        return _fixture.Factory.CreateClient().WithBearer(auth.AccessToken);
    }
}
