using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Crm;
using SourcingOps.Application.MasterData;

namespace SourcingOps.Api.Tests.Customers;

/// <summary>
/// Exercises ACTION_PLAN E4-01…E4-12 over real HTTP against the coordinator's binding
/// `/api/v1/customers` contract, through <see cref="AdminSeededFixture"/>'s already-provisioned
/// Super Admin/Associate clients — real Postgres (Testcontainers), real JWT auth pipeline, real
/// permission policies. Per the project's history (M1/M2's only real defects were wiring
/// between individually-passing components), this suite deliberately reads raw JSON in a few
/// places (Timeline shape) rather than trusting only strongly-typed deserialization to hide a
/// contract mismatch.
/// </summary>
public class CustomersEndpointTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public CustomersEndpointTests(AdminSeededFixture fixture)
    {
        _fixture = fixture;
    }

    // ---- Shared setup ------------------------------------------------------------

    private async Task<MasterDataAggregateDto> GetMasterDataAsync()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/master-data");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<MasterDataAggregateDto>())!;
    }

    private static CreateCustomerRequest ValidCifRequest(MasterDataAggregateDto md, string phone, Guid? ownerId = null) => new(
        Name: "Kundan Traders",
        BusinessName: "Kundan Traders Pvt Ltd",
        Phone: phone,
        Email: "kundan@example.com",
        City: "Surat",
        Region: "Gujarat",
        SourceChannel: "WhatsApp",
        ServiceTypeId: md.ServiceTypes.Single(s => s.Code == "CIF").Id,
        StatusId: md.LeadStatuses.OrderBy(s => s.SortOrder).First().Id,
        CategoryIds: [md.Categories.Single(c => c.Name == "Jewellery").Id],
        OwnerUserId: ownerId,
        Tags: ["VIP"],
        Notes: "Interested in bridal sets.",
        ExternalMarketplace: null,
        ExternalOrderRef: null,
        ExternalSupplierName: null,
        ExternalOrderValue: null,
        ExternalOrderCurrency: null,
        ExternalOrderDate: null,
        Gstin: null);

    private static string UniquePhone() => "90000" + Random.Shared.Next(10000, 99999);

    // ---- Create (E4-01…E4-05, E4-10, E4-12) -----------------------------------------

    [Fact]
    public async Task Create_ValidCifCustomer_Returns201_WithNormalizedPhoneAndCategoryIds()
    {
        var md = await GetMasterDataAsync();
        var request = ValidCifRequest(md, "098250-" + Random.Shared.Next(10000, 99999));

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CustomerDetailDto>();
        body!.Phone.Should().StartWith("+91").And.NotContain("-").And.NotContain(" ");
        body.CategoryIds.Should().Equal(md.Categories.Single(c => c.Name == "Jewellery").Id);
    }

    [Fact]
    public async Task Create_FreightOnlyWithExternalPurchaseFields_Returns201()
    {
        var md = await GetMasterDataAsync();
        var freightOnlyId = md.ServiceTypes.Single(s => s.Code == "FREIGHT_ONLY").Id;
        var request = ValidCifRequest(md, UniquePhone()) with
        {
            ServiceTypeId = freightOnlyId,
            ExternalMarketplace = "Alibaba",
            ExternalOrderRef = "ALI-99881",
            ExternalSupplierName = "Shenzhen Supplier Co",
            ExternalOrderValue = 2200.00m,
            ExternalOrderCurrency = "USD",
            ExternalOrderDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc)
        };

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CustomerDetailDto>();
        body!.ExternalMarketplace.Should().Be("Alibaba");
        body.ExternalOrderValue.Should().Be(2200.00m);
    }

    [Fact]
    public async Task Create_ExternalPurchaseFieldsOnCifCustomer_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var request = ValidCifRequest(md, UniquePhone()) with { ExternalMarketplace = "Alibaba" }; // still CIF

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Create_DuplicatePhone_Returns409_WithExistingCustomerInProblemExtensions_ThenOverrideSucceeds()
    {
        var md = await GetMasterDataAsync();
        var phone = UniquePhone();
        var first = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", ValidCifRequest(md, phone));
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstBody = await first.Content.ReadFromJsonAsync<CustomerDetailDto>();

        var duplicateAttempt = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", ValidCifRequest(md, phone) with { Name = "A Different Business" });

        duplicateAttempt.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problemJson = await duplicateAttempt.Content.ReadFromJsonAsync<JsonElement>();
        // ProblemDetails.Extensions is [JsonExtensionData] — System.Text.Json flattens its
        // entries onto the top-level object rather than nesting them under "extensions".
        problemJson.GetProperty("existingCustomer").GetProperty("id").GetGuid().Should().Be(firstBody!.Id);

        // FR-CRM-09/E4-10: explicit confirmDuplicate:true proceeds, no extra round trip.
        var overrideResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers",
            ValidCifRequest(md, phone) with { Name = "A Different Business", ConfirmDuplicate = true });
        overrideResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ---- GSTIN (N-37) ------------------------------------------------------------------

    [Fact]
    public async Task Create_WithGstin_RoundTripsOnDetail()
    {
        var md = await GetMasterDataAsync();
        var request = ValidCifRequest(md, UniquePhone()) with { Gstin = "27ABCDE1234F1Z5" };

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CustomerDetailDto>();
        body!.Gstin.Should().Be("27ABCDE1234F1Z5");
    }

    /// <summary>Optionality is the most important property of this field — omitting it must still succeed.</summary>
    [Fact]
    public async Task Create_WithoutGstin_Succeeds_StoredAsNull()
    {
        var md = await GetMasterDataAsync();
        var request = ValidCifRequest(md, UniquePhone()) with { Gstin = null };

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CustomerDetailDto>();
        body!.Gstin.Should().BeNull();
    }

    [Fact]
    public async Task Create_LowercaseGstinWithWhitespace_IsStoredTrimmedAndUppercased()
    {
        var md = await GetMasterDataAsync();
        var request = ValidCifRequest(md, UniquePhone()) with { Gstin = "  27abcde1234f1z5  " };

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CustomerDetailDto>();
        body!.Gstin.Should().Be("27ABCDE1234F1Z5");
    }

    [Fact]
    public async Task Create_BlankGstin_TrimsToNull()
    {
        var md = await GetMasterDataAsync();
        var request = ValidCifRequest(md, UniquePhone()) with { Gstin = "   " };

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CustomerDetailDto>();
        body!.Gstin.Should().BeNull();
    }

    [Theory]
    [InlineData("27ABCDE1234F1Z")]     // 14 chars
    [InlineData("27ABCDE1234F1Z55")]  // 16 chars
    [InlineData("27ABCDE1234F1Z-")]   // non-alphanumeric
    public async Task Create_InvalidGstin_Returns400_WithGstinFieldKey(string invalidGstin)
    {
        var md = await GetMasterDataAsync();
        var request = ValidCifRequest(md, UniquePhone()) with { Gstin = invalidGstin };

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problemJson = await response.Content.ReadFromJsonAsync<JsonElement>();
        problemJson.GetProperty("errors").TryGetProperty("gstin", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Update_InvalidGstin_Returns400_WithGstinFieldKey()
    {
        var md = await GetMasterDataAsync();
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", ValidCifRequest(md, UniquePhone()));
        var body = await created.Content.ReadFromJsonAsync<CustomerDetailDto>();

        var updateRequest = new UpdateCustomerRequest(
            body!.Name, body.BusinessName, body.Phone, body.Email, body.City, body.Region, body.SourceChannel,
            body.ServiceTypeId, body.StatusId, body.CategoryIds, body.Tags, body.Notes,
            body.ExternalMarketplace, body.ExternalOrderRef, body.ExternalSupplierName,
            body.ExternalOrderValue, body.ExternalOrderCurrency, body.ExternalOrderDate, "TOO-SHORT");

        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/customers/{body.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problemJson = await response.Content.ReadFromJsonAsync<JsonElement>();
        problemJson.GetProperty("errors").TryGetProperty("gstin", out _).Should().BeTrue();
    }

    /// <summary>
    /// Mandatory wire-name assertion (per this repo's convention — see AnalyticsEndpointTests):
    /// asserted against the RAW JSON string before deserialising, since ReadFromJsonAsync round
    /// trips through the same serializer on both ends and would never catch a wire-name mismatch.
    /// </summary>
    [Fact]
    public async Task Get_RawJson_ContainsGstinWireName()
    {
        var md = await GetMasterDataAsync();
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers",
            ValidCifRequest(md, UniquePhone()) with { Gstin = "27ABCDE1234F1Z5" });
        var createdBody = await created.Content.ReadFromJsonAsync<CustomerDetailDto>();

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/customers/{createdBody!.Id}");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        var raw = await response.Content.ReadAsStringAsync();

        raw.Should().Contain("\"gstin\"", "the Angular client reads this exact property name off GET /customers/{id}");
        raw.Should().Contain("27ABCDE1234F1Z5");
    }

    // ---- List / search / filter (E4-06) ----------------------------------------------

    [Fact]
    public async Task List_CombinedFilters_ReturnEnvelopeShapeAndMatchingResults()
    {
        var md = await GetMasterDataAsync();
        var uniqueRegion = $"TestRegion-{Guid.NewGuid():N}"[..20];
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers",
            ValidCifRequest(md, UniquePhone()) with { Region = uniqueRegion });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await created.Content.ReadFromJsonAsync<CustomerDetailDto>();

        var cifId = md.ServiceTypes.Single(s => s.Code == "CIF").Id;
        var statusId = md.LeadStatuses.OrderBy(s => s.SortOrder).First().Id;
        var categoryId = md.Categories.Single(c => c.Name == "Jewellery").Id;

        var response = await _fixture.AssociateClient.GetAsync(
            $"/api/v1/customers?region={Uri.EscapeDataString(uniqueRegion)}&serviceTypeId={cifId}&statusId={statusId}&categoryId={categoryId}&tag=VIP&page=1&pageSize=25");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CustomerListResultDto>();
        result!.Items.Should().ContainSingle(i => i.Id == createdBody!.Id);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(25);
        result.TotalCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task List_SearchByName_FindsCustomer()
    {
        var md = await GetMasterDataAsync();
        var uniqueName = $"Findable-Customer-{Guid.NewGuid():N}"[..30];
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", ValidCifRequest(md, UniquePhone()) with { Name = uniqueName });
        var createdBody = await created.Content.ReadFromJsonAsync<CustomerDetailDto>();

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/customers?search={Uri.EscapeDataString(uniqueName)}");

        var result = await response.Content.ReadFromJsonAsync<CustomerListResultDto>();
        result!.Items.Should().ContainSingle(i => i.Id == createdBody!.Id);
    }

    // ---- Get / Update ----------------------------------------------------------------

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/customers/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_ChangingStatus_RecordsStatusChangedOnTimeline()
    {
        var md = await GetMasterDataAsync();
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", ValidCifRequest(md, UniquePhone()));
        var body = await created.Content.ReadFromJsonAsync<CustomerDetailDto>();
        var newStatus = md.LeadStatuses.OrderBy(s => s.SortOrder).Skip(1).First();

        var updateRequest = new UpdateCustomerRequest(
            body!.Name, body.BusinessName, body.Phone, body.Email, body.City, body.Region, body.SourceChannel,
            body.ServiceTypeId, newStatus.Id, body.CategoryIds, body.Tags, body.Notes,
            body.ExternalMarketplace, body.ExternalOrderRef, body.ExternalSupplierName,
            body.ExternalOrderValue, body.ExternalOrderCurrency, body.ExternalOrderDate, body.Gstin);

        var updateResponse = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/customers/{body.Id}", updateRequest);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<CustomerDetailDto>();
        updated!.StatusId.Should().Be(newStatus.Id);

        var timelineResponse = await _fixture.AssociateClient.GetAsync($"/api/v1/customers/{body.Id}/timeline");
        var timeline = await timelineResponse.Content.ReadFromJsonAsync<List<TimelineEventDto>>();
        timeline.Should().ContainSingle(e => e.Kind == TimelineEventKinds.StatusChanged);
    }

    // ---- Interactions / follow-ups (E4-07, E4-08) ------------------------------------

    [Fact]
    public async Task AddInteraction_WithFollowUpDate_ThenAppearsInDueFollowUps()
    {
        var md = await GetMasterDataAsync();
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", ValidCifRequest(md, UniquePhone()) with { Name = $"FollowUp-{Guid.NewGuid():N}" });
        var body = await created.Content.ReadFromJsonAsync<CustomerDetailDto>();
        var followUpDate = DateTime.UtcNow.AddMinutes(-5);

        var interactionResponse = await _fixture.AssociateClient.PostAsJsonAsync($"/api/v1/customers/{body!.Id}/interactions",
            new CreateInteractionRequest("Call", "Call back about pricing.", followUpDate));

        interactionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var interaction = await interactionResponse.Content.ReadFromJsonAsync<InteractionDto>();
        interaction!.CustomerId.Should().Be(body.Id);

        var dueResponse = await _fixture.AssociateClient.GetAsync($"/api/v1/customers/follow-ups/due?asOf={Uri.EscapeDataString(DateTime.UtcNow.ToString("O"))}");
        dueResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var due = await dueResponse.Content.ReadFromJsonAsync<List<DueFollowUpDto>>();
        due!.Should().ContainSingle(d => d.CustomerId == body.Id);
    }

    [Fact]
    public async Task AddInteraction_ReservedType_Returns400()
    {
        var md = await GetMasterDataAsync();
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", ValidCifRequest(md, UniquePhone()));
        var body = await created.Content.ReadFromJsonAsync<CustomerDetailDto>();

        var response = await _fixture.AssociateClient.PostAsJsonAsync($"/api/v1/customers/{body!.Id}/interactions",
            new CreateInteractionRequest("StatusChange", "Fake.", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- Owner assignment (E4-09) ----------------------------------------------------

    [Fact]
    public async Task ChangeOwner_ReassignsAndRecordsOwnerChangedOnTimeline()
    {
        var md = await GetMasterDataAsync();
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", ValidCifRequest(md, UniquePhone()));
        var body = await created.Content.ReadFromJsonAsync<CustomerDetailDto>();

        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/customers/{body!.Id}/owner",
            new ChangeOwnerRequest(_fixture.AssociateAuth.User.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<CustomerDetailDto>();
        updated!.OwnerUserId.Should().Be(_fixture.AssociateAuth.User.Id);

        var timelineResponse = await _fixture.AssociateClient.GetAsync($"/api/v1/customers/{body.Id}/timeline");
        var timeline = await timelineResponse.Content.ReadFromJsonAsync<List<TimelineEventDto>>();
        timeline.Should().ContainSingle(e => e.Kind == TimelineEventKinds.OwnerChanged);
    }

    // ---- Timeline shape (E4-07) — the part most likely to go wrong ------------------

    [Fact]
    public async Task Timeline_ReturnsNewestFirst_IsoUtcTimestamps_AndNoColourField()
    {
        var md = await GetMasterDataAsync();
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", ValidCifRequest(md, UniquePhone()));
        var body = await created.Content.ReadFromJsonAsync<CustomerDetailDto>();
        await _fixture.AssociateClient.PostAsJsonAsync($"/api/v1/customers/{body!.Id}/interactions", new CreateInteractionRequest("Note", "Second event.", null));

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/customers/{body.Id}/timeline");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadFromJsonAsync<JsonElement>();
        var events = raw.EnumerateArray().ToList();

        events.Should().HaveCount(2);
        // Newest first: the manually-added note (created second) must come before EnquiryCaptured.
        events[0].GetProperty("kind").GetString().Should().Be(TimelineEventKinds.NoteAdded);
        events[1].GetProperty("kind").GetString().Should().Be(TimelineEventKinds.EnquiryCaptured);

        foreach (var evt in events)
        {
            // No colour/dot field anywhere in the payload — the frontend derives colour from `kind`.
            evt.TryGetProperty("dot", out _).Should().BeFalse("colour must not be duplicated in the payload — see the coordinator's explicit rule");
            evt.TryGetProperty("colour", out _).Should().BeFalse();
            evt.TryGetProperty("color", out _).Should().BeFalse();

            // occurredAtUtc must be a real ISO 8601 UTC instant, not a pre-formatted display string.
            var occurredAtRaw = evt.GetProperty("occurredAtUtc").GetString()!;
            occurredAtRaw.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$");
            DateTime.Parse(occurredAtRaw, null, System.Globalization.DateTimeStyles.RoundtripKind).Kind.Should().Be(DateTimeKind.Utc);
        }
    }

    [Fact]
    public async Task Timeline_UnknownCustomer_Returns404()
    {
        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/customers/{Guid.NewGuid()}/timeline");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- DoD: every permission-gated endpoint needs a 403-for-missing-permission test ---

    [Theory]
    [InlineData("GET", "/api/v1/customers")]
    [InlineData("GET", "/api/v1/customers/00000000-0000-0000-0000-000000000000")]
    [InlineData("GET", "/api/v1/customers/00000000-0000-0000-0000-000000000000/timeline")]
    [InlineData("GET", "/api/v1/customers/follow-ups/due")]
    public async Task NoPermissionCaller_Gets403_OnCustomersViewGatedEndpoints(string method, string path)
    {
        var client = await GetNoPermissionClientAsync();

        var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("POST", "/api/v1/customers")]
    [InlineData("PUT", "/api/v1/customers/00000000-0000-0000-0000-000000000000")]
    [InlineData("POST", "/api/v1/customers/00000000-0000-0000-0000-000000000000/interactions")]
    [InlineData("PUT", "/api/v1/customers/00000000-0000-0000-0000-000000000000/owner")]
    public async Task NoPermissionCaller_Gets403_OnCustomersEditGatedEndpoints(string method, string path)
    {
        var client = await GetNoPermissionClientAsync();

        var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> GetNoPermissionClientAsync()
    {
        // A dedicated user with an explicitly EMPTY role set — Associate cannot be used for
        // this proof because it already holds every non-Admin.* permission, INCLUDING
        // Customers.View/.Edit (TECH_SPEC §4.3), so it would pass every one of these checks.
        var auth = await AdminApiTestHelpers.ProvisionActiveUserAsync(
            _fixture.Factory, _fixture.AdminClient, roleIds: [], "No Perms User", Guid.NewGuid().ToString("N")[..8], "NoPerms-Changed-Pw1!");
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
