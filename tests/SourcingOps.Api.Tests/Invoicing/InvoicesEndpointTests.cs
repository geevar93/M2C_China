using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Admin;
using SourcingOps.Application.Crm;
using SourcingOps.Application.Invoicing;
using SourcingOps.Application.MasterData;

namespace SourcingOps.Api.Tests.Invoicing;

/// <summary>
/// Exercises ACTION_PLAN E8-01…E8-08 over real HTTP against a Testcontainers Postgres, per the
/// M6 contract. Pins the two wire-contract literals D-65 requires server-side (§4: bare-date
/// <c>invoiceDate</c> vs full-instant <c>paidAt</c>; status <c>code</c> values) and the two
/// absence traps §2 names explicitly (PUT /invoices/{id} ignores statusId/invoiceNumber/pdfFilePath;
/// PUT /status ignores a paid marker) — every identity assertion terminates in a literal, never
/// in the constant it is checking (D-64).
/// </summary>
public class InvoicesEndpointTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public InvoicesEndpointTests(AdminSeededFixture fixture)
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
            // State code 24 matches the seller's in EnsureCompanySettingsConfiguredAsync, so this
            // suite's default supply is intra-state. Without it, issuing now 400s by design.
            Gstin: null, StateCode: "24", ConfirmDuplicate: true);

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", request);
        await response.EnsureSuccessOrThrowWithBodyAsync();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<CreateInvoiceRequest> ValidCreateAsync(MasterDataAggregateDto md) =>
        new(await CreateCustomerAsync(md), null, new DateOnly(2026, 8, 1), "Consulting services", "INR", [Line(1000m, 18m)]);

    /// <summary>
    /// One line at qty 1 x <paramref name="unitPrice"/> at <paramref name="gstRate"/>%, carrying
    /// an HSN so the issue-time completeness gate is satisfied. Reproduces the figures the
    /// hand-entered Amount/TaxAmount used to supply, now that both are derived from the lines.
    /// </summary>
    private static UpsertInvoiceLineRequest Line(decimal unitPrice, decimal gstRate) =>
        new(null, "Consulting services", "998311", 1m, unitPrice, gstRate);


    private async Task<InvoiceDetailDto> PostInvoiceAsync(CreateInvoiceRequest request)
    {
        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/invoices", request);
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<InvoiceDetailDto>())!;
    }

    private async Task EnsureCompanySettingsConfiguredAsync()
    {
        var request = new UpsertCompanySettingsRequest(
            "M2C Sourcing Pvt Ltd", "24AAAAA0000A1Z5", "24", "123 Industrial Estate, Surat, Gujarat",
            "M2C Sourcing", "000123456789", "HDFC0000123", "Surat Main", "INV", null);
        var response = await _fixture.AdminClient.PutAsJsonAsync("/api/v1/admin/company-settings", request);
        await response.EnsureSuccessOrThrowWithBodyAsync();
    }

    // ---- Create (E8-01) -------------------------------------------------------------------

    [Fact]
    public async Task Create_Valid_Returns201_AsDraft_WithGeneratedNumber()
    {
        var md = await GetMasterDataAsync();

        var body = await PostInvoiceAsync(await ValidCreateAsync(md));

        body.InvoiceNumber.Should().MatchRegex(@"^INV-\d{4}-\d{3}$", "D-i's pattern, reused verbatim for invoices (M6 contract §1)");
        body.Status.Code.Should().Be("DRAFT");
        body.TotalAmount.Should().Be(1180m);
        body.HasPdf.Should().BeFalse();
    }

    [Fact]
    public async Task Create_UnknownCustomer_Returns400ProblemDetails()
    {
        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/invoices",
            new CreateInvoiceRequest(Guid.NewGuid(), null, new DateOnly(2026, 8, 1), null, "INR", [Line(100m, 0m)]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Create_WireContract_InvoiceDateIsABareDateString_CreatedAtIsAFullInstant()
    {
        var md = await GetMasterDataAsync();
        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/invoices", await ValidCreateAsync(md));
        await response.EnsureSuccessOrThrowWithBodyAsync();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        // D-65: pinned server-side, not just in a client fixture. invoiceDate carries no time
        // component; createdAt is a full ISO-8601 instant (M6 contract §4).
        root.GetProperty("invoiceDate").GetString().Should().Be("2026-08-01");
        root.GetProperty("createdAt").GetString().Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}");
        root.TryGetProperty("paidAt", out var paidAt).Should().BeTrue();
        (paidAt.ValueKind == JsonValueKind.Null).Should().BeTrue("a freshly created Draft invoice is never paid");
    }

    // ---- List (E8-05) ----------------------------------------------------------------------

    [Fact]
    public async Task List_DefaultPageSize_Is25()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/invoices");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<InvoiceListResultDto>())!;
        body.PageSize.Should().Be(25, "M6 contract §3 corrected the M5 20-default mistake — must not regress to 20");
    }

    [Fact]
    public async Task List_ReturnsPerStatusCountsForEveryConfiguredStatus()
    {
        var md = await GetMasterDataAsync();
        await PostInvoiceAsync(await ValidCreateAsync(md));

        var response = await _fixture.AssociateClient.GetAsync("/api/v1/invoices?pageSize=5");

        var body = (await response.Content.ReadFromJsonAsync<InvoiceListResultDto>())!;
        body.StatusCounts.Select(c => c.Code).Should().BeEquivalentTo(md.InvoiceStatuses.Select(s => s.Code));
        body.StatusCounts.Sum(c => c.Count).Should().Be(body.TotalCount);
    }

    /// <summary>N-31/D-72: <c>totalAmount</c> travels over the wire on the same status-counts row as <c>count</c>.</summary>
    [Fact]
    public async Task List_StatusCounts_CarryTotalAmountOverTheWire()
    {
        var md = await GetMasterDataAsync();
        await PostInvoiceAsync(await ValidCreateAsync(md)); // 1000 + 180

        var response = await _fixture.AssociateClient.GetAsync("/api/v1/invoices");

        // Assert the RAW wire name first. Deserialising into InvoiceListResultDto below round-trips
        // through the same serializer on both ends, so a camelCase/PascalCase mismatch with the
        // Angular client would be completely invisible to it — the client reads `totalAmount` off
        // untyped JSON and would silently see `undefined`. This is the §16.3 class of defect: the
        // value is computed correctly, stored correctly and transmitted, and is wrong only at the
        // point where a different runtime reads it.
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("\"totalAmount\":", "the Angular client's InvoiceStatusCount reads this exact property name");

        var body = (await response.Content.ReadFromJsonAsync<InvoiceListResultDto>())!;
        var draft = body.StatusCounts.Single(c => c.Code == "DRAFT");
        draft.TotalAmount.Should().BeGreaterThanOrEqualTo(1180m, "at least the invoice this test created contributes to the DRAFT total");

        var paid = body.StatusCounts.Single(c => c.Code == "PAID");
        if (paid.Count == 0)
        {
            paid.TotalAmount.Should().Be(0m, "a zero-count status must zero-fill its total, not omit it or leave it null");
        }
    }

    [Fact]
    public async Task List_SearchOnInvoiceNumber_FindsTheInvoice()
    {
        var md = await GetMasterDataAsync();
        var created = await PostInvoiceAsync(await ValidCreateAsync(md));

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/invoices?search={created.InvoiceNumber}");

        var body = (await response.Content.ReadFromJsonAsync<InvoiceListResultDto>())!;
        body.Items.Should().Contain(i => i.Id == created.Id);
    }

    [Fact]
    public async Task List_InvertedDateRange_Returns400ProblemDetails()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/invoices?fromDate=2026-08-01&toDate=2026-07-01");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- Get -----------------------------------------------------------------------------

    [Fact]
    public async Task Get_UnknownInvoice_Returns404()
    {
        (await _fixture.AssociateClient.GetAsync($"/api/v1/invoices/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Update — the two D-42/D-43-style traps (M6 contract §2) --------------------------

    [Fact]
    public async Task Update_WhileDraft_Succeeds()
    {
        var md = await GetMasterDataAsync();
        var created = await PostInvoiceAsync(await ValidCreateAsync(md));

        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/invoices/{created.Id}",
            new UpdateInvoiceRequest(created.Customer.Id, null, created.InvoiceDate, "Revised description", "INR", [Line(2000m, 18m)]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<InvoiceDetailDto>())!;
        body.Amount.Should().Be(2000m);
    }

    [Fact]
    public async Task Update_RawPayloadCarryingStatusIdInvoiceNumberAndPdfFilePath_IgnoresAllThree()
    {
        var md = await GetMasterDataAsync();
        var created = await PostInvoiceAsync(await ValidCreateAsync(md));

        // Deliberately raw JSON — UpdateInvoiceRequest has no statusId/invoiceNumber/pdfFilePath
        // properties to bind into, so a client attempting to smuggle them through must find
        // them silently dropped, not applied. This is the regression the M6 contract's §2 traps
        // (mirroring D-42/D-43) exist to catch.
        var raw = new
        {
            customerId = created.Customer.Id,
            shipmentId = (Guid?)null,
            invoiceDate = created.InvoiceDate,
            lineDescription = "Attempted smuggle",
            // amount/taxAmount are no longer request properties at all — both are derived from
            // the lines. Left in the raw payload deliberately: they must be dropped exactly like
            // the three server-owned fields below, not applied over the computed figures.
            amount = 500m,
            taxAmount = 90m,
            currency = "INR",
            lines = new[]
            {
                new { inventoryItemId = (Guid?)null, description = "Consulting services", hsnCode = "998311", quantity = 1m, unitPrice = 100m, gstRate = 18m }
            },
            statusId = md.InvoiceStatuses.Single(s => s.Code == "ISSUED").Id,
            invoiceNumber = "INV-HACKED-001",
            pdfFilePath = "/etc/passwd"
        };

        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/invoices/{created.Id}", raw);
        await response.EnsureSuccessOrThrowWithBodyAsync();

        var body = (await response.Content.ReadFromJsonAsync<InvoiceDetailDto>())!;
        body.Status.Code.Should().Be("DRAFT", "PUT /invoices/{id} must never move status");
        body.InvoiceNumber.Should().NotBe("INV-HACKED-001");
        body.HasPdf.Should().BeFalse();
        // Derived from the one line (100 @ 18%), NOT the 500/90 the payload tried to assert.
        body.Amount.Should().Be(100m);
        body.TaxAmount.Should().Be(18m);
    }

    [Fact]
    public async Task Update_AfterIssued_Returns409ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        await EnsureCompanySettingsConfiguredAsync();
        var created = await PostInvoiceAsync(await ValidCreateAsync(md));
        var issuedId = md.InvoiceStatuses.Single(s => s.Code == "ISSUED").Id;
        var issue = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/invoices/{created.Id}/status",
            new ChangeInvoiceStatusRequest(issuedId, null));
        await issue.EnsureSuccessOrThrowWithBodyAsync();

        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/invoices/{created.Id}",
            new UpdateInvoiceRequest(created.Customer.Id, null, created.InvoiceDate, "x", "INR", [Line(1m, 0m)]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Update_UnknownInvoice_Returns404()
    {
        var md = await GetMasterDataAsync();
        var customerId = await CreateCustomerAsync(md);
        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/invoices/{Guid.NewGuid()}",
            new UpdateInvoiceRequest(customerId, null, new DateOnly(2026, 8, 1), null, "INR", [Line(1m, 0m)]));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Status transitions (E8-02, E8-03) -------------------------------------------------
    //
    // NOTE: "without CompanySettings configured" is NOT tested in this class — CompanySettings
    // is a genuine cross-test singleton, and several tests below (via
    // EnsureCompanySettingsConfiguredAsync) configure it on the SAME shared Postgres container
    // this whole class's AdminSeededFixture provisions, in an order xUnit does not guarantee.
    // See InvoicesWithoutCompanySettingsEndpointTests, which gets its OWN fixture (a fresh
    // Testcontainers Postgres per test class) specifically so nothing else can have configured
    // the row first.

    [Fact]
    public async Task ChangeStatus_DraftToIssued_WithCompanySettings_RendersPdf_DownloadableAfterward()
    {
        var md = await GetMasterDataAsync();
        await EnsureCompanySettingsConfiguredAsync();
        var created = await PostInvoiceAsync(await ValidCreateAsync(md));
        var issuedId = md.InvoiceStatuses.Single(s => s.Code == "ISSUED").Id;

        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/invoices/{created.Id}/status",
            new ChangeInvoiceStatusRequest(issuedId, "Sent to customer"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<InvoiceDetailDto>())!;
        body.Status.Code.Should().Be("ISSUED");
        body.HasPdf.Should().BeTrue();

        var pdfResponse = await _fixture.AssociateClient.GetAsync($"/api/v1/invoices/{created.Id}/pdf");
        pdfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        pdfResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        var bytes = await pdfResponse.Content.ReadAsByteArrayAsync();
        bytes.Take(4).Should().BeEquivalentTo(new byte[] { 0x25, 0x50, 0x44, 0x46 }, "a real %PDF magic-byte header, not a stub");
    }

    [Fact]
    public async Task ChangeStatus_RawPayloadCarryingAPaidMarker_IsIgnored_InvoiceStaysIssuedNotPaid()
    {
        var md = await GetMasterDataAsync();
        await EnsureCompanySettingsConfiguredAsync();
        var created = await PostInvoiceAsync(await ValidCreateAsync(md));
        var issuedId = md.InvoiceStatuses.Single(s => s.Code == "ISSUED").Id;

        // ChangeInvoiceStatusRequest has no paid-marker property — a client attempting to sneak
        // one through PUT /status (rather than the separate, separately-permissioned
        // POST /mark-paid) must find it dropped (M6 contract §2's second trap).
        var raw = new { statusId = issuedId, note = "Issuing", paidAt = DateTime.UtcNow, paidReference = "SNUCK-IN" };
        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/invoices/{created.Id}/status", raw);
        await response.EnsureSuccessOrThrowWithBodyAsync();

        var body = (await response.Content.ReadFromJsonAsync<InvoiceDetailDto>())!;
        body.Status.Code.Should().Be("ISSUED", "PUT /status alone must never move an invoice to PAID");
        body.PaidReference.Should().BeNull();
        body.PaidAt.Should().BeNull();
    }

    [Theory]
    [InlineData("DRAFT", "PAID")]
    [InlineData("ISSUED", "DRAFT")]
    public async Task ChangeStatus_IllegalTransition_Returns409ProblemDetails_NamingBothCodes(string fromCode, string toCode)
    {
        var md = await GetMasterDataAsync();
        await EnsureCompanySettingsConfiguredAsync();
        var created = await PostInvoiceAsync(await ValidCreateAsync(md));

        if (fromCode == "ISSUED")
        {
            var issue = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/invoices/{created.Id}/status",
                new ChangeInvoiceStatusRequest(md.InvoiceStatuses.Single(s => s.Code == "ISSUED").Id, null));
            await issue.EnsureSuccessOrThrowWithBodyAsync();
        }

        var targetId = md.InvoiceStatuses.Single(s => s.Code == toCode).Id;
        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/invoices/{created.Id}/status",
            new ChangeInvoiceStatusRequest(targetId, null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("fromStatus").GetString().Should().Be(fromCode);
        doc.RootElement.GetProperty("toStatus").GetString().Should().Be(toCode);
    }

    [Fact]
    public async Task ChangeStatus_UnknownInvoice_Returns404()
    {
        var md = await GetMasterDataAsync();
        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/invoices/{Guid.NewGuid()}/status",
            new ChangeInvoiceStatusRequest(md.InvoiceStatuses.First().Id, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Mark paid (E8-07) -----------------------------------------------------------------

    [Fact]
    public async Task MarkPaid_WhileIssued_Returns200_WithPaidAtAndReference()
    {
        var md = await GetMasterDataAsync();
        await EnsureCompanySettingsConfiguredAsync();
        var created = await PostInvoiceAsync(await ValidCreateAsync(md));
        var issue = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/invoices/{created.Id}/status",
            new ChangeInvoiceStatusRequest(md.InvoiceStatuses.Single(s => s.Code == "ISSUED").Id, null));
        await issue.EnsureSuccessOrThrowWithBodyAsync();

        var response = await _fixture.AssociateClient.PostAsJsonAsync($"/api/v1/invoices/{created.Id}/mark-paid",
            new MarkInvoicePaidRequest(null, "NEFT-99887"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<InvoiceDetailDto>())!;
        body.Status.Code.Should().Be("PAID");
        body.PaidReference.Should().Be("NEFT-99887");
        body.PaidAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkPaid_WhileDraft_Returns409ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var created = await PostInvoiceAsync(await ValidCreateAsync(md));

        var response = await _fixture.AssociateClient.PostAsJsonAsync($"/api/v1/invoices/{created.Id}/mark-paid",
            new MarkInvoicePaidRequest(null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- PDF download (E8-03) --------------------------------------------------------------

    [Fact]
    public async Task DownloadPdf_BeforeIssued_Returns409ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var created = await PostInvoiceAsync(await ValidCreateAsync(md));

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/invoices/{created.Id}/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DownloadPdf_UnknownInvoice_Returns404()
    {
        (await _fixture.AssociateClient.GetAsync($"/api/v1/invoices/{Guid.NewGuid()}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- DoD: 403 per endpoint --------------------------------------------------------------

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnList()
    {
        var client = await GetNoPermissionClientAsync();
        (await client.GetAsync("/api/v1/invoices")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnGet()
    {
        var client = await GetNoPermissionClientAsync();
        (await client.GetAsync($"/api/v1/invoices/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnCreate()
    {
        var client = await GetNoPermissionClientAsync();
        var response = await client.PostAsJsonAsync("/api/v1/invoices",
            new CreateInvoiceRequest(Guid.NewGuid(), null, new DateOnly(2026, 8, 1), null, "INR", [Line(1m, 0m)]));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnUpdate()
    {
        var client = await GetNoPermissionClientAsync();
        var response = await client.PutAsJsonAsync($"/api/v1/invoices/{Guid.NewGuid()}",
            new UpdateInvoiceRequest(Guid.NewGuid(), null, new DateOnly(2026, 8, 1), null, "INR", [Line(1m, 0m)]));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnChangeStatus()
    {
        var client = await GetNoPermissionClientAsync();
        var response = await client.PutAsJsonAsync($"/api/v1/invoices/{Guid.NewGuid()}/status",
            new ChangeInvoiceStatusRequest(Guid.NewGuid(), null));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnMarkPaid()
    {
        var client = await GetNoPermissionClientAsync();
        var response = await client.PostAsJsonAsync($"/api/v1/invoices/{Guid.NewGuid()}/mark-paid",
            new MarkInvoicePaidRequest(null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnDownloadPdf()
    {
        var client = await GetNoPermissionClientAsync();
        (await client.GetAsync($"/api/v1/invoices/{Guid.NewGuid()}/pdf")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // NOTE: the DoD also implies "a caller with Invoicing.Edit but NOT Invoicing.MarkPaid gets
    // 403 on mark-paid" — genuinely untestable with the two seeded roles (Associate holds every
    // non-Admin.* permission, including MarkPaid, as one bloc; SuperAdmin holds everything).
    // Same limitation N-15 already records for other permission splits. Not faked with a
    // trivial always-passing test; flagged in the build report instead.

    private async Task<HttpClient> GetNoPermissionClientAsync()
    {
        var auth = await AdminApiTestHelpers.ProvisionActiveUserAsync(
            _fixture.Factory, _fixture.AdminClient, roleIds: [], "No Perms Invoicing User", Guid.NewGuid().ToString("N")[..8], "NoPerms-Changed-Pw1!");
        return _fixture.Factory.CreateClient().WithBearer(auth.AccessToken);
    }
}

/// <summary>
/// A SEPARATE test class purely so xUnit provisions a SEPARATE <see cref="AdminSeededFixture"/>
/// (its own Testcontainers Postgres — <c>IClassFixture</c> instantiates once per class, never
/// shared across classes) — see the note in <see cref="InvoicesEndpointTests"/> for why "no
/// CompanySettings configured yet" cannot safely share a container with tests that configure it.
/// </summary>
public class InvoicesWithoutCompanySettingsEndpointTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public InvoicesWithoutCompanySettingsEndpointTests(AdminSeededFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ChangeStatus_DraftToIssued_WithoutCompanySettings_Returns400ProblemDetails()
    {
        var mdResponse = await _fixture.AssociateClient.GetAsync("/api/v1/master-data");
        await mdResponse.EnsureSuccessOrThrowWithBodyAsync();
        var md = (await mdResponse.Content.ReadFromJsonAsync<MasterDataAggregateDto>())!;

        var customerRequest = new CreateCustomerRequest(
            Name: "Meena Patel", BusinessName: null, Phone: $"+9190{Random.Shared.NextInt64(10000000, 99999999)}",
            Email: null, City: null, Region: null, SourceChannel: null,
            ServiceTypeId: md.ServiceTypes.Single(s => s.Code == "CIF").Id,
            StatusId: md.LeadStatuses.First().Id,
            CategoryIds: null, OwnerUserId: null, Tags: null, Notes: null,
            ExternalMarketplace: null, ExternalOrderRef: null, ExternalSupplierName: null,
            ExternalOrderValue: null, ExternalOrderCurrency: null, ExternalOrderDate: null,
            Gstin: null, ConfirmDuplicate: true);
        var customerResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", customerRequest);
        await customerResponse.EnsureSuccessOrThrowWithBodyAsync();
        using var customerDoc = JsonDocument.Parse(await customerResponse.Content.ReadAsStringAsync());
        var customerId = customerDoc.RootElement.GetProperty("id").GetGuid();

        var createResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/invoices",
            new CreateInvoiceRequest(customerId, null, new DateOnly(2026, 8, 1), "Consulting services", "INR",
                [new UpsertInvoiceLineRequest(null, "Consulting services", "998311", 1m, 1000m, 18m)]));
        await createResponse.EnsureSuccessOrThrowWithBodyAsync();
        var created = (await createResponse.Content.ReadFromJsonAsync<InvoiceDetailDto>())!;

        var issuedId = md.InvoiceStatuses.Single(s => s.Code == "ISSUED").Id;
        var response = await _fixture.AssociateClient.PutAsJsonAsync($"/api/v1/invoices/{created.Id}/status",
            new ChangeInvoiceStatusRequest(issuedId, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
