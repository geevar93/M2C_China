namespace SourcingOps.Application.Analytics;

/// <summary>ACTION_PLAN E10-01…E10-07/E10-10. Read-only aggregates behind <c>PermissionCodes.AnalyticsView</c> — see <c>AnalyticsController</c>.</summary>
public interface IAnalyticsService
{
    Task<LeadsAnalyticsDto> GetLeadsAsync(AnalyticsQuery query, CancellationToken ct = default);
    Task<ServiceSplitAnalyticsDto> GetServiceSplitAsync(AnalyticsQuery query, CancellationToken ct = default);
    Task<CategoryMixAnalyticsDto> GetCategoryMixAsync(AnalyticsQuery query, CancellationToken ct = default);
    Task<VendorAnalyticsDto> GetVendorsAsync(AnalyticsQuery query, CancellationToken ct = default);
    Task<InventoryAnalyticsDto> GetInventoryAsync(AnalyticsQuery query, CancellationToken ct = default);
    Task<DispatchAnalyticsDto> GetDispatchAsync(AnalyticsQuery query, CancellationToken ct = default);
}
