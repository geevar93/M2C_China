using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Catalog;
using SourcingOps.Application.Crm;
using SourcingOps.Application.Dispatching;
using SourcingOps.Application.Invoicing;
using SourcingOps.Application.MasterData;
using SourcingOps.Application.Vendors;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Tests.Dispatching;

/// <summary>
/// Exercises ACTION_PLAN E9-01, E9-02, E9-06, E9-07 over real HTTP against
/// <see cref="AdminSeededFixture"/>'s already-provisioned Super Admin/Associate clients — real
/// Postgres (Testcontainers), real JWT auth pipeline, real permission policies. E9-08 (the
/// answered FSD Q4 — any staff may dispatch) is asserted here at the token level in addition to
/// the seed-level unit test in <c>DbSeederTests</c>: <see cref="Fixture"/>'s Associate account
/// is a completely ordinary seeded Associate, and its JWT's resolved permission set must carry
/// <see cref="PermissionCodes.DispatchSend"/> for any of this suite's non-403 tests to pass.
/// </summary>
public class DispatchEndpointTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public DispatchEndpointTests(AdminSeededFixture fixture)
    {
        _fixture = fixture;
    }

    // ---- Shared setup ------------------------------------------------------------------

    private async Task<MasterDataAggregateDto> GetMasterDataAsync()
    {
        var response = await _fixture.AssociateClient.GetAsync("/api/v1/master-data");
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<MasterDataAggregateDto>())!;
    }

    private async Task<CustomerDetailDto> CreateCustomerAsync(MasterDataAggregateDto md)
    {
        var request = new CreateCustomerRequest(
            Name: "Kundan Traders", BusinessName: null, Phone: "9" + Random.Shared.Next(100000000, 999999999),
            Email: null, City: null, Region: null, SourceChannel: null,
            ServiceTypeId: md.ServiceTypes.Single(s => s.Code == "CIF").Id,
            StatusId: md.LeadStatuses.OrderBy(s => s.SortOrder).First().Id,
            CategoryIds: null, OwnerUserId: null, Tags: null, Notes: null,
            ExternalMarketplace: null, ExternalOrderRef: null, ExternalSupplierName: null,
            ExternalOrderValue: null, ExternalOrderCurrency: null, ExternalOrderDate: null,
            Gstin: null);
        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/customers", request);
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<CustomerDetailDto>())!;
    }

    private async Task<CatalogDocumentDto> CreateCatalogDocumentAsync(MasterDataAggregateDto md)
    {
        var vendorRequest = new CreateVendorRequest(
            Name: $"Dispatch-Vendor-{Guid.NewGuid():N}", ContactPerson: null, Phone: null, Email: null,
            Region: "Yiwu, Zhejiang", StatusId: md.VendorStatuses.Single(s => s.Code == "ACTIVE").Id,
            CategoryIds: null, Moq: null, LeadTime: null, PaymentTerms: null, ReliabilityRating: null, Notes: null);
        var vendorResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/vendors", vendorRequest);
        await vendorResponse.EnsureSuccessOrThrowWithBodyAsync();
        var vendor = (await vendorResponse.Content.ReadFromJsonAsync<VendorDetailDto>())!;

        var sectionResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/catalog-sections",
            new CreateCatalogSectionRequest(vendor.Id, "Spring 2026 Collection", md.Categories.First().Id, null));
        await sectionResponse.EnsureSuccessOrThrowWithBodyAsync();
        var section = (await sectionResponse.Content.ReadFromJsonAsync<CatalogSectionDto>())!;

        using var form = new MultipartFormDataContent();
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4 dispatch test content");
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "catalog.pdf");
        var uploadResponse = await _fixture.AssociateClient.PostAsync($"/api/v1/catalog-sections/{section.Id}/documents", form);
        await uploadResponse.EnsureSuccessOrThrowWithBodyAsync();
        return (await uploadResponse.Content.ReadFromJsonAsync<CatalogDocumentDto>())!;
    }

    // ---- E9-08 (token-level check that the seeded Associate really can dispatch) -------

    [Fact]
    public void AssociateToken_CarriesDispatchSendPermission_PerFsdQ4AnsweredInOI8()
    {
        _fixture.AssociateAuth.User.Permissions.Should().Contain(PermissionCodes.DispatchSend);
    }

    // ---- Compose (E9-01, E9-06) ---------------------------------------------------------

    [Fact]
    public async Task Compose_ValidCustomerAndDocument_Returns200_WithRenderedMessageAndWaMeLink()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var document = await CreateCatalogDocumentAsync(md);

        var response = await _fixture.AssociateClient.GetAsync(
            $"/api/v1/dispatch-log/compose?customerId={customer.Id}&catalogDocumentId={document.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DispatchComposeDto>();
        body!.Message.Should().Contain("Kundan Traders").And.Contain("Spring 2026 Collection");
        body.DeepLinkUrl.Should().StartWith("https://wa.me/");
        body.DeepLinkUrl.Should().NotContain("+").And.NotContain(" ");
    }

    [Fact]
    public async Task Compose_UnknownCustomer_Returns404()
    {
        var md = await GetMasterDataAsync();
        var document = await CreateCatalogDocumentAsync(md);

        var response = await _fixture.AssociateClient.GetAsync(
            $"/api/v1/dispatch-log/compose?customerId={Guid.NewGuid()}&catalogDocumentId={document.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Compose_UnknownCatalogDocument_Returns404()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);

        var response = await _fixture.AssociateClient.GetAsync(
            $"/api/v1/dispatch-log/compose?customerId={customer.Id}&catalogDocumentId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Create (E9-02) ------------------------------------------------------------------

    [Fact]
    public async Task Create_ValidDispatch_Returns201_WithStaffUserFromToken_NotFromBody()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var document = await CreateCatalogDocumentAsync(md);
        var request = new CreateDispatchLogRequest(customer.Id, document.Id, null, "Hi, here is our catalog.");

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/dispatch-log", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<DispatchLogDto>();
        body!.CustomerId.Should().Be(customer.Id);
        body.CatalogDocumentId.Should().Be(document.Id);
        body.StaffUserId.Should().Be(_fixture.AssociateAuth.User.Id); // from the token, since the request body carries no staff field
        body.Message.Should().Be("Hi, here is our catalog.");
    }

    [Fact]
    public async Task Create_UnknownCustomerId_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var document = await CreateCatalogDocumentAsync(md);
        var request = new CreateDispatchLogRequest(Guid.NewGuid(), document.Id, null, "Hi there");

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/dispatch-log", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Create_UnknownCatalogDocumentId_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var request = new CreateDispatchLogRequest(customer.Id, Guid.NewGuid(), null, "Hi there");

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/dispatch-log", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Create_BlankMessage_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var document = await CreateCatalogDocumentAsync(md);
        var request = new CreateDispatchLogRequest(customer.Id, document.Id, null, "   ");

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/dispatch-log", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- M6/E8-06: invoice dispatch --------------------------------------------------------

    private async Task<InvoiceDetailDto> CreateInvoiceAsync(Guid customerId)
    {
        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/invoices",
            new CreateInvoiceRequest(customerId, null, new DateOnly(2026, 8, 1), "Consulting services", 1000m, 180m, "INR"));
        await response.EnsureSuccessOrThrowWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<InvoiceDetailDto>())!;
    }

    /// <summary>
    /// M6 contract §7 asked to verify whether <c>POST /dispatch-log</c> accepts an invoice
    /// reference, and to flag it as a real gap if it did not. It did not (<c>Dispatch.CatalogDocumentId</c>
    /// was non-nullable with no invoice counterpart) — fixed in this pass; this proves it end to end.
    /// </summary>
    [Fact]
    public async Task Create_InvoiceTarget_Returns201_WithCatalogFieldsNull_AndInvoiceFieldsPopulated()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var invoice = await CreateInvoiceAsync(customer.Id);
        var request = new CreateDispatchLogRequest(customer.Id, null, invoice.Id, "Sharing your invoice.");

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/dispatch-log", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<DispatchLogDto>())!;
        body.CatalogDocumentId.Should().BeNull();
        body.CatalogName.Should().BeNull();
        body.InvoiceId.Should().Be(invoice.Id);
        body.InvoiceNumber.Should().Be(invoice.InvoiceNumber);
    }

    [Fact]
    public async Task Create_NeitherCatalogNorInvoiceSupplied_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var request = new CreateDispatchLogRequest(customer.Id, null, null, "Hi there");

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/dispatch-log", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_BothCatalogAndInvoiceSupplied_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var document = await CreateCatalogDocumentAsync(md);
        var invoice = await CreateInvoiceAsync(customer.Id);
        var request = new CreateDispatchLogRequest(customer.Id, document.Id, invoice.Id, "Hi there");

        var response = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/dispatch-log", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_InvoiceDispatch_ThenCustomerTimeline_ShowsAnEventReferencingTheInvoice()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var invoice = await CreateInvoiceAsync(customer.Id);
        var dispatchResponse = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/dispatch-log",
            new CreateDispatchLogRequest(customer.Id, null, invoice.Id, "Sharing your invoice."));
        await dispatchResponse.EnsureSuccessOrThrowWithBodyAsync();

        var timelineResponse = await _fixture.AssociateClient.GetAsync($"/api/v1/customers/{customer.Id}/timeline");
        await timelineResponse.EnsureSuccessOrThrowWithBodyAsync();
        var timeline = (await timelineResponse.Content.ReadFromJsonAsync<List<TimelineEventDto>>())!;

        var dispatchEvent = timeline.Should().ContainSingle(e => e.RefType == "Invoice" && e.RefId == invoice.Id
            && e.Title == "Invoice sent").Subject;
        dispatchEvent.Body.Should().Contain(invoice.InvoiceNumber);
        // Also proves the InvoiceCreated event this same invoice generates, since both now
        // share this customer's timeline (E8-04).
        timeline.Should().Contain(e => e.Kind == TimelineEventKinds.InvoiceCreated && e.RefId == invoice.Id);
    }

    // ---- M4 exit criterion: dispatch shows up on the customer timeline (ACTION_PLAN §5) ----

    [Fact]
    public async Task Create_ThenCustomerTimeline_ShowsCatalogDispatchedEvent()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var document = await CreateCatalogDocumentAsync(md);
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/dispatch-log",
            new CreateDispatchLogRequest(customer.Id, document.Id, null, "Hi, here is our catalog."));
        await created.EnsureSuccessOrThrowWithBodyAsync();

        var timelineResponse = await _fixture.AssociateClient.GetAsync($"/api/v1/customers/{customer.Id}/timeline");

        timelineResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var timeline = await timelineResponse.Content.ReadFromJsonAsync<List<TimelineEventDto>>();
        var dispatchEvent = timeline.Should().ContainSingle(e => e.Kind == TimelineEventKinds.CatalogDispatched).Subject;
        dispatchEvent.RefType.Should().Be("CatalogDocument");
        dispatchEvent.RefId.Should().Be(document.Id);
        dispatchEvent.Body.Should().Contain("Spring 2026 Collection");
    }

    /// <summary>
    /// D-67 regression. An INVOICE dispatch must surface as its own `InvoiceDispatched` kind,
    /// not as `CatalogDispatched`. Asserted with a string literal rather than the constant
    /// (D-64): a test written against `TimelineEventKinds.InvoiceDispatched` would still pass
    /// if someone repointed that constant at "CatalogDispatched", which is exactly the
    /// regression this exists to catch. Both halves are pinned — a distinct kind AND the
    /// shared dot-colour family is NOT asserted here because colour lives only in the client.
    /// </summary>
    [Fact]
    public async Task Create_InvoiceTarget_ThenCustomerTimeline_ShowsInvoiceDispatchedEvent_NotCatalogDispatched()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var invoice = await CreateInvoiceAsync(customer.Id);
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/dispatch-log",
            new CreateDispatchLogRequest(customer.Id, null, invoice.Id, "Hi, here is your invoice."));
        await created.EnsureSuccessOrThrowWithBodyAsync();

        var timelineResponse = await _fixture.AssociateClient.GetAsync($"/api/v1/customers/{customer.Id}/timeline");

        timelineResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var timeline = await timelineResponse.Content.ReadFromJsonAsync<List<TimelineEventDto>>();
        var dispatchEvent = timeline.Should().ContainSingle(e => e.Kind == "InvoiceDispatched").Subject;
        dispatchEvent.RefType.Should().Be("Invoice");
        dispatchEvent.RefId.Should().Be(invoice.Id);
        dispatchEvent.Body.Should().Contain(invoice.InvoiceNumber);
        timeline!.Should().NotContain(e => e.Kind == "CatalogDispatched",
            "an invoice send is not a catalog send — D-67");
    }

    // ---- Sent-to history (E9-07) ----------------------------------------------------------

    [Fact]
    public async Task SentTo_UnknownDocument_Returns404()
    {
        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/catalog-documents/{Guid.NewGuid()}/dispatches");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SentTo_DocumentWithNoDispatches_Returns200_Empty()
    {
        var md = await GetMasterDataAsync();
        var document = await CreateCatalogDocumentAsync(md);

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/catalog-documents/{document.Id}/dispatches");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<DispatchHistoryEntryDto>>();
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task SentTo_AfterDispatch_ListsCustomerAndStaffAndTimestamp()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var document = await CreateCatalogDocumentAsync(md);
        var created = await _fixture.AssociateClient.PostAsJsonAsync("/api/v1/dispatch-log",
            new CreateDispatchLogRequest(customer.Id, document.Id, null, "Hi, here is our catalog."));
        await created.EnsureSuccessOrThrowWithBodyAsync();

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/catalog-documents/{document.Id}/dispatches");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<DispatchHistoryEntryDto>>();
        body.Should().ContainSingle(d => d.CustomerId == customer.Id && d.StaffUserId == _fixture.AssociateAuth.User.Id);
    }

    // ---- E9-10: the temporary public share link ------------------------------------------

    /// <summary>
    /// Pulls the token back out of the composed URL. The URL is absolute against the configured
    /// public origin (<c>Cors:FrontendOrigin</c> in this factory), which the in-memory test
    /// server cannot be asked to fetch — but the path it points at is this same app's route, so
    /// requesting it relatively exercises exactly the endpoint a real phone would hit.
    /// </summary>
    private static string TokenFrom(DocumentShareLinkDto link) => link.Url.Split('/').Last();

    [Fact]
    public async Task Compose_ReturnsAShareLink_WhoseUrlIsAlsoEmbeddedInTheMessage()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var document = await CreateCatalogDocumentAsync(md);

        var response = await _fixture.AssociateClient.GetAsync(
            $"/api/v1/dispatch-log/compose?customerId={customer.Id}&catalogDocumentId={document.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<DispatchComposeDto>())!;
        body.ShareLink.Should().NotBeNull();
        body.ShareLink.Url.Should().Contain("/api/v1/shared-documents/");
        body.ShareLink.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow.AddHours(47));
        body.Message.Should().Contain(body.ShareLink.Url);
    }

    /// <summary>
    /// The whole point of E9-10, proven over real HTTP: a caller with **no** Authorization header
    /// gets the PDF. Every other document route in this API answers such a caller with 401
    /// (E6-04 asserts exactly that for catalog documents), so this test is the one place that
    /// deliberate exception is demonstrated rather than described.
    /// </summary>
    [Fact]
    public async Task SharedDocument_AnonymousCallerWithALiveToken_Gets200Pdf()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var document = await CreateCatalogDocumentAsync(md);
        var compose = await _fixture.AssociateClient.GetFromJsonAsync<DispatchComposeDto>(
            $"/api/v1/dispatch-log/compose?customerId={customer.Id}&catalogDocumentId={document.Id}");

        using var anonymous = _fixture.Factory.CreateClient(); // no bearer token, deliberately
        var response = await anonymous.GetAsync($"/api/v1/shared-documents/{TokenFrom(compose!.ShareLink)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition?.DispositionType.Should().Be("inline");
        (await response.Content.ReadAsStringAsync()).Should().StartWith("%PDF");
    }

    [Fact]
    public async Task SharedDocument_SameTokenTwice_BothSucceed()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var document = await CreateCatalogDocumentAsync(md);
        var compose = await _fixture.AssociateClient.GetFromJsonAsync<DispatchComposeDto>(
            $"/api/v1/dispatch-log/compose?customerId={customer.Id}&catalogDocumentId={document.Id}");
        var token = TokenFrom(compose!.ShareLink);

        using var anonymous = _fixture.Factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/shared-documents/{token}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymous.GetAsync($"/api/v1/shared-documents/{token}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// A wrong token must be 404 — never 401. A 401 would tell the recipient to log in (they
    /// have no account and never will in Phase 1, FSD A1) and would tell a prober that
    /// credentials are the thing standing between them and the document.
    /// </summary>
    [Theory]
    [InlineData("not-a-real-token")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task SharedDocument_UnknownToken_Gets404_Never401(string token)
    {
        using var anonymous = _fixture.Factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/v1/shared-documents/{token}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().BeEmpty();
    }

    [Fact]
    public async Task SharedDocument_RevokedToken_Gets404()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var document = await CreateCatalogDocumentAsync(md);
        var compose = await _fixture.AssociateClient.GetFromJsonAsync<DispatchComposeDto>(
            $"/api/v1/dispatch-log/compose?customerId={customer.Id}&catalogDocumentId={document.Id}");
        var token = TokenFrom(compose!.ShareLink);

        using var anonymous = _fixture.Factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/shared-documents/{token}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var revoke = await _fixture.AssociateClient.PostAsync(
            $"/api/v1/dispatch-log/share-links/{compose.ShareLink.Id}/revoke", null);
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterRevoke = await anonymous.GetAsync($"/api/v1/shared-documents/{token}");
        afterRevoke.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RevokeShareLink_UnknownId_Returns404()
    {
        var response = await _fixture.AssociateClient.PostAsync(
            $"/api/v1/dispatch-log/share-links/{Guid.NewGuid()}/revoke", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RevokeShareLink_Twice_IsIdempotent()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var document = await CreateCatalogDocumentAsync(md);
        var compose = await _fixture.AssociateClient.GetFromJsonAsync<DispatchComposeDto>(
            $"/api/v1/dispatch-log/compose?customerId={customer.Id}&catalogDocumentId={document.Id}");

        var first = await _fixture.AssociateClient.PostAsync($"/api/v1/dispatch-log/share-links/{compose!.ShareLink.Id}/revoke", null);
        var second = await _fixture.AssociateClient.PostAsync($"/api/v1/dispatch-log/share-links/{compose.ShareLink.Id}/revoke", null);

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Compose_NeitherTargetSupplied_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);

        var response = await _fixture.AssociateClient.GetAsync($"/api/v1/dispatch-log/compose?customerId={customer.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Compose_BothTargetsSupplied_Returns400ProblemDetails()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var document = await CreateCatalogDocumentAsync(md);
        var invoice = await CreateInvoiceAsync(customer.Id);

        var response = await _fixture.AssociateClient.GetAsync(
            $"/api/v1/dispatch-log/compose?customerId={customer.Id}&catalogDocumentId={document.Id}&invoiceId={invoice.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// E9-10 closes the compose-side half of E8-06: the dispatch log has accepted an invoice
    /// target since M6, but compose could not prepare one, which is why the invoice screen's
    /// WhatsApp button is still inert. A Draft invoice has no PDF, so it must fail here — at the
    /// staff member's screen, where it can be fixed by issuing the invoice — rather than as a
    /// dead link in the customer's chat.
    /// </summary>
    [Fact]
    public async Task Compose_DraftInvoice_Returns400_BecauseNoPdfExistsYet()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);
        var invoice = await CreateInvoiceAsync(customer.Id); // created Draft, never issued

        var response = await _fixture.AssociateClient.GetAsync(
            $"/api/v1/dispatch-log/compose?customerId={customer.Id}&invoiceId={invoice.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Compose_UnknownInvoice_Returns404()
    {
        var md = await GetMasterDataAsync();
        var customer = await CreateCustomerAsync(md);

        var response = await _fixture.AssociateClient.GetAsync(
            $"/api/v1/dispatch-log/compose?customerId={customer.Id}&invoiceId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- DoD: every permission-gated endpoint needs a 403-for-missing-permission test -----

    private async Task<HttpClient> GetNoPermissionClientAsync()
    {
        var auth = await AdminApiTestHelpers.ProvisionActiveUserAsync(
            _fixture.Factory, _fixture.AdminClient, roleIds: [], "No Perms Dispatch User", Guid.NewGuid().ToString("N")[..8], "NoPerms-Changed-Pw1!");
        return _fixture.Factory.CreateClient().WithBearer(auth.AccessToken);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnCompose()
    {
        var client = await GetNoPermissionClientAsync();

        var response = await client.GetAsync($"/api/v1/dispatch-log/compose?customerId={Guid.NewGuid()}&catalogDocumentId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnCreate()
    {
        var client = await GetNoPermissionClientAsync();
        var request = new CreateDispatchLogRequest(Guid.NewGuid(), Guid.NewGuid(), null, "Hi there");

        var response = await client.PostAsJsonAsync("/api/v1/dispatch-log", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnSentToHistory()
    {
        var client = await GetNoPermissionClientAsync();

        var response = await client.GetAsync($"/api/v1/catalog-documents/{Guid.NewGuid()}/dispatches");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// E9-10: minting a share link is gated even though consuming one is not. Handing out an
    /// unauthenticated URL to a business document is a send-class action, so it takes the same
    /// permission as sending.
    /// </summary>
    [Fact]
    public async Task NoPermissionCaller_Gets403_OnRevokeShareLink()
    {
        var client = await GetNoPermissionClientAsync();

        var response = await client.PostAsync($"/api/v1/dispatch-log/share-links/{Guid.NewGuid()}/revoke", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
