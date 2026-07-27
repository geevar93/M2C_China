namespace SourcingOps.Application.MasterData;

/// <summary>
/// Implements ACTION_PLAN E3-03…E3-09: CRUD, retire/restore, reorder and referential-safety
/// delete across every configurable master-data collection, cached (E3-09) and invalidated
/// on every write. See <see cref="MasterDataService"/> for the implementation notes.
/// </summary>
public interface IMasterDataService
{
    /// <summary>The single aggregate read the frontend loads once at startup.</summary>
    Task<MasterDataAggregateDto> GetAggregateAsync(bool includeRetired, CancellationToken ct = default);

    Task<IMasterDataItemDto> CreateAsync(MasterDataCollectionKey key, UpsertMasterDataRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Null if <paramref name="id"/> does not exist in <paramref name="key"/>'s collection.</summary>
    Task<IMasterDataItemDto?> UpdateAsync(MasterDataCollectionKey key, Guid id, UpsertMasterDataRequest request, Guid actorUserId, CancellationToken ct = default);

    Task<IMasterDataItemDto?> RetireAsync(MasterDataCollectionKey key, Guid id, Guid actorUserId, CancellationToken ct = default);

    Task<IMasterDataItemDto?> RestoreAsync(MasterDataCollectionKey key, Guid id, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Returns the whole collection in its new order. Throws <see cref="Common.AppValidationException"/> if any id is unknown.</summary>
    Task<IReadOnlyList<IMasterDataItemDto>> ReorderAsync(MasterDataCollectionKey key, IReadOnlyList<ReorderItemDto> items, Guid actorUserId, CancellationToken ct = default);

    /// <summary>E3-08: deletes only if genuinely unreferenced; otherwise returns a conflict result naming retire as the alternative.</summary>
    Task<MasterDataDeleteResult> DeleteAsync(MasterDataCollectionKey key, Guid id, Guid actorUserId, CancellationToken ct = default);
}
