namespace SourcingOps.Application.Vendors;

/// <summary>Implements ACTION_PLAN E5-01…E5-06 behind the coordinator's binding <c>/api/v1/vendors</c> contract.</summary>
public interface IVendorService
{
    Task<VendorListResultDto> ListAsync(VendorListQuery query, CancellationToken ct = default);

    /// <summary>Null return means the vendor id was not found. Embeds catalog sections and their documents (E5-05).</summary>
    Task<VendorDetailDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<VendorDetailDto> CreateAsync(CreateVendorRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Null return means the vendor id was not found.</summary>
    Task<VendorDetailDto?> UpdateAsync(Guid id, UpdateVendorRequest request, Guid actorUserId, CancellationToken ct = default);
}
