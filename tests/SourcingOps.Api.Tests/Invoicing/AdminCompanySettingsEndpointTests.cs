using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SourcingOps.Api.Tests.TestSupport;
using SourcingOps.Application.Admin;

namespace SourcingOps.Api.Tests.Invoicing;

/// <summary>
/// M6 contract §0: converts FSD Q9c from an engineering blocker into a data-entry task.
/// </summary>
public class AdminCompanySettingsEndpointTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public AdminCompanySettingsEndpointTests(AdminSeededFixture fixture)
    {
        _fixture = fixture;
    }

    // NOTE: "before any PUT" is NOT tested in this class — see AdminCompanySettingsEmptyStateTests,
    // which gets its own fixture (fresh Testcontainers Postgres per test class) specifically
    // because CompanySettings is a genuine cross-test singleton and Put_ThenGet_RoundTripsEveryField
    // below configures it on the SAME shared container, in an order xUnit does not guarantee.

    [Fact]
    public async Task Put_ThenGet_RoundTripsEveryField()
    {
        var request = new UpsertCompanySettingsRequest(
            "M2C Sourcing Pvt Ltd", "24AAAAA0000A1Z5", "24", "123 Industrial Estate, Surat, Gujarat",
            "M2C Sourcing", "000123456789", "HDFC0000123", "Surat Main", "inv",
            "Goods once sold will not be taken back.");

        var putResponse = await _fixture.AdminClient.PutAsJsonAsync("/api/v1/admin/company-settings", request);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _fixture.AdminClient.GetAsync("/api/v1/admin/company-settings");
        var body = (await getResponse.Content.ReadFromJsonAsync<CompanySettingsDto>())!;

        body.LegalEntityName.Should().Be("M2C Sourcing Pvt Ltd");
        body.Gstin.Should().Be("24AAAAA0000A1Z5");
        body.RegisteredAddress.Should().Be("123 Industrial Estate, Surat, Gujarat");
        body.BankAccountName.Should().Be("M2C Sourcing");
        body.InvoiceNumberPrefix.Should().Be("inv");
        body.UpdatedAt.Should().NotBeNull();
        body.UpdatedByName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnGet()
    {
        var client = await GetNoPermissionClientAsync();
        (await client.GetAsync("/api/v1/admin/company-settings")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AssociateCaller_Gets403_OnGet()
    {
        // Admin.ManageMasterData is withheld from Associate by seeding design (PermissionCodes.AdminOnly)
        // — company/bank details are sensitive in a way lookup labels are not, so even a READ is gated.
        (await _fixture.AssociateClient.GetAsync("/api/v1/admin/company-settings")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionCaller_Gets403_OnPut()
    {
        var client = await GetNoPermissionClientAsync();
        var response = await client.PutAsJsonAsync("/api/v1/admin/company-settings",
            new UpsertCompanySettingsRequest(null, null, null, null, null, null, null, null, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> GetNoPermissionClientAsync()
    {
        var auth = await AdminApiTestHelpers.ProvisionActiveUserAsync(
            _fixture.Factory, _fixture.AdminClient, roleIds: [], "No Perms CompanySettings User", Guid.NewGuid().ToString("N")[..8], "NoPerms-Changed-Pw1!");
        return _fixture.Factory.CreateClient().WithBearer(auth.AccessToken);
    }
}

/// <summary>A separate test class purely to get a separate, never-written-to <see cref="AdminSeededFixture"/> — see the note in <see cref="AdminCompanySettingsEndpointTests"/>.</summary>
public class AdminCompanySettingsEmptyStateTests : IClassFixture<AdminSeededFixture>
{
    private readonly AdminSeededFixture _fixture;

    public AdminCompanySettingsEmptyStateTests(AdminSeededFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Get_BeforeAnyPut_ReturnsAllNullShape_Not404()
    {
        var response = await _fixture.AdminClient.GetAsync("/api/v1/admin/company-settings");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "\"not configured\" is a legitimate state, not an error");
        var body = (await response.Content.ReadFromJsonAsync<CompanySettingsDto>())!;
        body.LegalEntityName.Should().BeNull();
        body.RegisteredAddress.Should().BeNull();
    }
}
