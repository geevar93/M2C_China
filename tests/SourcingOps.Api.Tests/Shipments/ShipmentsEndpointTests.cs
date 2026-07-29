using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Crm;
using SourcingOps.Application.Inventory;
using SourcingOps.Application.MasterData;
using SourcingOps.Application.Shipments;

namespace SourcingOps.Api.Tests.Shipments;

/// <summary>
/// Exercises ACTION_PLAN E7-05…E7-08 and E7-10 over real HTTP against a Testcontainers
/// Postgres. This is where the D-i reference-generation and D-g 409 paths are proven against a
/// real relational provider — neither can be exercised by the Application-layer unit tests,
/// which run on the EF Core InMemory provider and therefore enforce no unique index.
///
/// Every endpoint carries a matching 403 test, per the §7 DoD.
/// </summary>
public class ShipmentsEndpointTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public ShipmentsEndpointTests(AdminSeededFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<MasterDataAggregateDto> GetMasterDataAsync()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/master-data");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<MasterDataAggregateDto>())!;
    }

    private async Task<Guid> CreateCustomerAsync(MasterDataAggregateDto md)
    {
        var request = new CreateCustomerRequest(
            Name: $"Meena Patel {Guid.NewGuid():N}"[..24],
            BusinessName: $"Meena Traders {Guid.NewGuid():N}"[..24],
            Phone: $"+9190{Random.Shared.NextInt64(10000000, 99999999)}",
            Email: null, City: "Surat", Region: "Gujarat", SourceChannel: null,
            ServiceTypeId: md.ServiceTypes.Single(s => s.Code == "CIF").Id,
            StatusId: md.LeadStatuses.First().Id,
            CategoryIds: null, OwnerUserId: null, Tags: null, Notes: null,
            ExternalMarketplace: null, ExternalOrderRef: null, ExternalSupplierName: null,
            ExternalOrderValue: null, ExternalOrderCurrency: null, ExternalOrderDate: null,
            ConfirmDuplicate: true);

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", request);
        await response.EnsureSuccessOrThrowWithBodyAsync();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<InventoryItemDto> CreateItemAsync(MasterDataAggregateDto md, decimal onHand, decimal? unitCost = 500m)
    {
        var request = new CreateInventoryItemRequest(
            $"Ship-Item-{Guid.NewGuid():N}", null, null,
            md.Categories.First(c => c.Name == "Jewellery").Id, null, "set", onHand, 10m, unitCost);
        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/inventory", request);
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<InventoryItemDto>())!;
    }

    private async Task<decimal> OnHandAsync(Guid itemId)
    {
        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/inventory/{itemId}");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<InventoryItemDto>())!.OnHandQty;
    }

    private async Task<CreateShipmentRequest> ValidCreateAsync(
        MasterDataAggregateDto md, IReadOnlyList<ShipmentLineRequest>? lines = null, string serviceTypeCode = "CIF", bool allowNegative = false)
    {
        var customerId = await CreateCustomerAsync(md);
        return new CreateShipmentRequest(
            customerId, "Surat, Gujarat",
            md.ServiceTypes.Single(s => s.Code == serviceTypeCode).Id,
            new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc),
            md.ShipmentStatuses.Single(s => s.Code == "PACKED").Id,
            FreightCost: 48_000m, TotalValue: null, Mode: "Sea LCL · Nhava Sheva",
            AwbOrBl: "BL SNKO4471192", Eta: new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc),
            Lines: lines, AllowNegativeStock: allowNegative);
    }

    private async Task<ShipmentDetailDto> PostShipmentAsync(CreateShipmentRequest request)
    {
        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/shipments", request);
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<ShipmentDetailDto>())!;
    }

    // ---- Create (E7-05, E7-06, D-i) --------------------------------------------------------------

    [Fact]
    public async Task Create_CifWithLines_Returns201_GeneratesReference_AndDecrementsStock()
    {
        var md = await GetMasterDataAsync();
        var item = await CreateItemAsync(md, onHand: 100m);

        var body = await PostShipmentAsync(await ValidCreateAsync(md, [new ShipmentLineRequest(item.Id, 30m, null)]));

        body.Reference.Should().MatchRegex(@"^SHP-\d{4}-\d{3}$", "D-i's SHP-YYMM-NNN format");
        body.Status.Code.Should().Be("PACKED");
        body.ServiceType.Code.Should().Be("CIF");
        body.Lines.Should().ContainSingle().Which.UnitCost.Should().Be(500m, "snapshotted from the item (D-b)");
        body.TotalValue.Should().Be(15_000m, "server-computed from the lines (D-c)");
        body.StatusHistory.Should().ContainSingle("creation seeds an opening history row (D-e)");
        body.RecordedByName.Should().NotBeNullOrWhiteSpace();

        (await OnHandAsync(item.Id)).Should().Be(70m);
    }

    [Fact]
    public async Task Create_TwoShipmentsInTheSameMonth_GetSequentialUniqueReferences()
    {
        var md = await GetMasterDataAsync();

        var first = await PostShipmentAsync(await ValidCreateAsync(md));
        var second = await PostShipmentAsync(await ValidCreateAsync(md));

        // Proven against the real partial unique index, which the InMemory provider does not enforce.
        first.Reference.Should().NotBe(second.Reference);
        var firstSeq = int.Parse(first.Reference!.Split('-')[2]);
        var secondSeq = int.Parse(second.Reference!.Split('-')[2]);
        secondSeq.Should().Be(firstSeq + 1);
    }

    [Fact]
    public async Task Create_ConcurrentRequests_AllGetDistinctReferences()
    {
        var md = await GetMasterDataAsync();
        var requests = new List<CreateShipmentRequest>();
        for (var i = 0; i < 5; i++)
        {
            requests.Add(await ValidCreateAsync(md));
        }

        // Fired together so the reference generator's 23505 retry (D-i) is actually exercised
        // rather than merely present.
        var responses = await Task.WhenAll(requests.Select(r => _fixture.AssociateClient.PostAsJsonAsync("/api/v1/shipments", r)));

        foreach (var response in responses)
        {
            await response.EnsureSuccessOrThrowWithBodyAsync();
        }

        var references = new List<string>();
        foreach (var response in responses)
        {
            references.Add((await response.Content.ReadFromJsonAsync<ShipmentDetailDto>())!.Reference!);
        }

        references.Should().OnlyHaveUniqueItems();
    }

    // ---- E7-06 / D-g: the 409 and its override --------------------------------------------------------

    [Fact]
    public async Task Create_WouldDriveStockNegative_Returns409ProblemDetails_NamingTheItemAndQuantities()
    {
        var md = await GetMasterDataAsync();
        var item = await CreateItemAsync(md, onHand: 100m);

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/shipments",
            await ValidCreateAsync(md, [new ShipmentLineRequest(item.Id, 140m, null)]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var conflict = doc.RootElement.GetProperty("insufficientStock")[0];
        conflict.GetProperty("inventoryItemId").GetGuid().Should().Be(item.Id);
        conflict.GetProperty("itemName").GetString().Should().Be(item.Name);
        conflict.GetProperty("requestedQty").GetDecimal().Should().Be(140m);
        conflict.GetProperty("availableQty").GetDecimal().Should().Be(100m);

        (await OnHandAsync(item.Id)).Should().Be(100m, "nothing is committed when the guard trips");
    }

    [Fact]
    public async Task Create_WithAllowNegativeStock_Succeeds_AndDrivesStockNegative()
    {
        var md = await GetMasterDataAsync();
        var item = await CreateItemAsync(md, onHand: 100m);

        var body = await PostShipmentAsync(await ValidCreateAsync(md, [new ShipmentLineRequest(item.Id, 140m, null)], allowNegative: true));

        body.Lines.Should().ContainSingle();
        (await OnHandAsync(item.Id)).Should().Be(-40m);

        var itemResponse = await _fixture.AssociateClient.GetAsync($"/api/v1/inventory/{item.Id}");
        (await itemResponse.Content.ReadFromJsonAsync<InventoryItemDto>())!.StockLevel.Should().Be("NEGATIVE");
    }

    // ---- E7-10 / D-h: freight-only ----------------------------------------------------------------------

    [Fact]
    public async Task Create_FreightOnlyWithLines_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var item = await CreateItemAsync(md, onHand: 100m);

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/shipments",
            await ValidCreateAsync(md, [new ShipmentLineRequest(item.Id, 1m, null)], serviceTypeCode: "FREIGHT_ONLY"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Create_FreightOnlyWithoutLines_Succeeds_MovesNoStock_AndKeepsTheClientTotal()
    {
        var md = await GetMasterDataAsync();
        var item = await CreateItemAsync(md, onHand: 100m);

        var request = await ValidCreateAsync(md, serviceTypeCode: "FREIGHT_ONLY");
        var body = await PostShipmentAsync(request with { TotalValue = 64_500m });

        body.ServiceType.Code.Should().Be("FREIGHT_ONLY");
        body.Lines.Should().BeEmpty();
        body.TotalValue.Should().Be(64_500m);
        (await OnHandAsync(item.Id)).Should().Be(100m);
    }

    // ---- Get / Update / Delete -----------------------------------------------------------------------------

    [Fact]
    public async Task Get_KnownShipment_Returns200_WithLinesHistoryAndDocuments()
    {
        var md = await GetMasterDataAsync();
        var item = await CreateItemAsync(md, onHand: 100m);
        var created = await PostShipmentAsync(await ValidCreateAsync(md, [new ShipmentLineRequest(item.Id, 5m, null)]));

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/shipments/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<ShipmentDetailDto>())!;
        body.Lines.Should().ContainSingle();
        body.StatusHistory.Should().ContainSingle();
        body.Documents.Should().BeEmpty();
        body.Customer.Name.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Get_UnknownShipment_Returns404()
    {
        (await _fixture.AssociateClient.GetAsync($"/api/v1/shipments/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_LineDelta_IsAppliedRatherThanAFreshDecrement()
    {
        var md = await GetMasterDataAsync();
        var item = await CreateItemAsync(md, onHand: 100m);
        var created = await PostShipmentAsync(await ValidCreateAsync(md, [new ShipmentLineRequest(item.Id, 30m, null)]));
        (await OnHandAsync(item.Id)).Should().Be(70m);

        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/shipments/{created.Id}",
            new UpdateShipmentRequest(created.Customer.Id, "Pune", created.ServiceType.Id, created.DispatchDate,
                null, null, null, null, null, [new ShipmentLineRequest(item.Id, 50m, null)]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<ShipmentDetailDto>())!;
        body.Destination.Should().Be("Pune");
        body.Lines.Should().ContainSingle().Which.Quantity.Should().Be(50m);
        (await OnHandAsync(item.Id)).Should().Be(50m, "only the extra 20 is consumed (D-j)");
    }

    [Fact]
    public async Task Update_RemovingAllLines_RestoresTheStock()
    {
        var md = await GetMasterDataAsync();
        var item = await CreateItemAsync(md, onHand: 100m);
        var created = await PostShipmentAsync(await ValidCreateAsync(md, [new ShipmentLineRequest(item.Id, 30m, null)]));

        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/shipments/{created.Id}",
            new UpdateShipmentRequest(created.Customer.Id, "Pune", created.ServiceType.Id, created.DispatchDate,
                null, null, null, null, null, Lines: null));

        await response.EnsureSuccessOrThrowWithBodyAsync();
        (await OnHandAsync(item.Id)).Should().Be(100m);
    }

    [Fact]
    public async Task Update_UnknownShipment_Returns404()
    {
        var md = await GetMasterDataAsync();
        var customerId = await CreateCustomerAsync(md);
        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/shipments/{Guid.NewGuid()}",
            new UpdateShipmentRequest(customerId, null, md.ServiceTypes.First().Id, null, null, null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_RestoresStock_Returns204_ThenGetReturns404()
    {
        var md = await GetMasterDataAsync();
        var item = await CreateItemAsync(md, onHand: 100m);
        var created = await PostShipmentAsync(await ValidCreateAsync(md, [new ShipmentLineRequest(item.Id, 30m, null)]));

        var response = await _fixture.AssociateClient.DeleteAsync($"/api/v1/shipments/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await OnHandAsync(item.Id)).Should().Be(100m);
        (await _fixture.AssociateClient.GetAsync($"/api/v1/shipments/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_UnknownShipment_Returns404()
    {
        (await _fixture.AssociateClient.DeleteAsync($"/api/v1/shipments/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- E7-07 / D-e: status transitions --------------------------------------------------------------------

    [Fact]
    public async Task ChangeStatus_AppendsHistoryWithTimestampAndUser()
    {
        var md = await GetMasterDataAsync();
        var created = await PostShipmentAsync(await ValidCreateAsync(md));
        var inTransitId = md.ShipmentStatuses.Single(s => s.Code == "IN TRANSIT").Id;

        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/shipments/{created.Id}/status",
            new ChangeShipmentStatusRequest(inTransitId, "Loaded at Nhava Sheva"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<ShipmentDetailDto>())!;
        body.Status.Code.Should().Be("IN TRANSIT");
        body.StatusHistory.Should().HaveCount(2);
        body.StatusHistory.Select(h => h.Status.Code).Should().ContainInOrder("PACKED", "IN TRANSIT");
        body.StatusHistory[1].Note.Should().Be("Loaded at Nhava Sheva");
        body.StatusHistory[1].ChangedByName.Should().NotBeNullOrWhiteSpace();
        body.StatusHistory.Select(h => h.ChangedAt).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ChangeStatus_ToTheStatusAlreadyHeld_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var created = await PostShipmentAsync(await ValidCreateAsync(md));

        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/shipments/{created.Id}/status",
            new ChangeShipmentStatusRequest(created.Status.Id, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task ChangeStatus_UnknownShipment_Returns404()
    {
        var md = await GetMasterDataAsync();
        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/shipments/{Guid.NewGuid()}/status",
            new ChangeShipmentStatusRequest(md.ShipmentStatuses.First().Id, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- E7-08: list, filters and status tab counts ------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsPerStatusCountsForEveryConfiguredStatus()
    {
        var md = await GetMasterDataAsync();
        await PostShipmentAsync(await ValidCreateAsync(md));

        var response = await _fixture.AssociateClient.GetAsync("/api/v1/shipments?pageSize=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<ShipmentListResultDto>())!;
        body.StatusCounts.Select(c => c.Code).Should().BeEquivalentTo(md.ShipmentStatuses.Select(s => s.Code));
        body.StatusCounts.Sum(c => c.Count).Should().Be(body.TotalCount);
    }

    [Fact]
    public async Task List_StatusFilter_NarrowsItemsButNotTheCounts()
    {
        var md = await GetMasterDataAsync();
        var created = await PostShipmentAsync(await ValidCreateAsync(md));
        var inTransitId = md.ShipmentStatuses.Single(s => s.Code == "IN TRANSIT").Id;
        var moved = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/shipments/{created.Id}/status",
            new ChangeShipmentStatusRequest(inTransitId, null));
        await moved.EnsureSuccessOrThrowWithBodyAsync();

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/shipments?statusId={inTransitId}&pageSize=200");

        var body = (await response.Content.ReadFromJsonAsync<ShipmentListResultDto>())!;
        body.Items.Should().OnlyContain(i => i.Status.Code == "IN TRANSIT");
        body.StatusCounts.Sum(c => c.Count).Should().BeGreaterThan(body.TotalCount,
            "the tab counts span the whole set, so they must exceed the filtered page total once other statuses exist");
    }

    [Fact]
    public async Task List_SearchOnReference_FindsTheShipment()
    {
        var md = await GetMasterDataAsync();
        var created = await PostShipmentAsync(await ValidCreateAsync(md));

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/shipments?search={created.Reference}");

        var body = (await response.Content.ReadFromJsonAsync<ShipmentListResultDto>())!;
        body.Items.Should().Contain(i => i.Id == created.Id);
    }

    [Fact]
    public async Task List_InvertedDateRange_Returns400ProblemDetails()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/shipments?from=2026-08-01&to=2026-07-01");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task List_DateRange_FiltersOnDispatchDate()
    {
        var md = await GetMasterDataAsync();
        var created = await PostShipmentAsync(await ValidCreateAsync(md));

        var inRange = await _fixture.AssociateClient.GetAsync("/api/v1/shipments?from=2026-07-01&to=2026-07-31&pageSize=200");
        var outOfRange = await _fixture.AssociateClient.GetAsync("/api/v1/shipments?from=2026-01-01&to=2026-01-31&pageSize=200");

        (await inRange.Content.ReadFromJsonAsync<ShipmentListResultDto>())!.Items.Should().Contain(i => i.Id == created.Id);
        (await outOfRange.Content.ReadFromJsonAsync<ShipmentListResultDto>())!.Items.Should().NotContain(i => i.Id == created.Id);
    }

    // ---- DoD: 403 per endpoint ------------------------------------------------------------------------------------

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnList()
    {
        var client = await GetNoPermissionClientAsync();
        (await client.GetAsync("/api/v1/shipments")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnGet()
    {
        var client = await GetNoPermissionClientAsync();
        (await client.GetAsync($"/api/v1/shipments/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnCreate()
    {
        var client = await GetNoPermissionClientAsync();
        var response = await client.PostAsJsonAsync("/api/v1/shipments",
            new CreateShipmentRequest(Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(), null, null, null, null, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnUpdate()
    {
        var client = await GetNoPermissionClientAsync();
        var response = await client.PutAsJsonAsync($"/api/v1/shipments/{Guid.NewGuid()}",
            new UpdateShipmentRequest(Guid.NewGuid(), null, Guid.NewGuid(), null, null, null, null, null, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnDelete()
    {
        var client = await GetNoPermissionClientAsync();
        (await client.DeleteAsync($"/api/v1/shipments/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnChangeStatus()
    {
        var client = await GetNoPermissionClientAsync();
        var response = await client.PutAsJsonAsync($"/api/v1/shipments/{Guid.NewGuid()}/status",
            new ChangeShipmentStatusRequest(Guid.NewGuid(), null));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnonymousCaller_Gets401_OnList()
    {
        using var client = _fixture.Factory.CreateClient();
        (await client.GetAsync("/api/v1/shipments")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpClient> GetNoPermissionClientAsync()
    {
        var auth = await AdminApiTestHelpers.ProvisionActiveUserAsync(
            _fixture.Factory, _fixture.AdminClient, roleIds: [], "No Perms Shipment User", Guid.NewGuid().ToString("N")[..8], "NoPerms-Changed-Pw1!");
        return _fixture.Factory.CreateClient().WithBearer(auth.AccessToken);
    }
}
