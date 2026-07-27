namespace SourcingOps.Application.Crm;

/// <summary>
/// Plain settings POCO — same "not <c>IOptions&lt;T&gt;</c>, keep Application framework-light"
/// pattern as <see cref="Auth.AuthOptions"/>. Backs FR-CRM-04 / ACTION_PLAN E4-04: the
/// platform accepts loose phone input at intake and normalises it to E.164 so it is directly
/// usable to build a `wa.me` link with no further transformation (E9-01 depends on this). The
/// default country is configurable rather than hard-coded per the coordinator's brief — see
/// <see cref="Common.PhoneNumberNormalizer"/>.
/// </summary>
public sealed class CustomerOptions
{
    /// <summary>
    /// E.164 calling-code prefix (e.g. "+91") applied to a bare national number when no
    /// country code is present in the input. Config key: <c>Customers:DefaultCountryPhoneCode</c>.
    /// </summary>
    public string DefaultCountryPhoneCode { get; set; } = "+91";
}
