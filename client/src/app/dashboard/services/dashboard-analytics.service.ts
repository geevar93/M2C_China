import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import {
  AnalyticsQueryParams,
  CategoryMixAnalytics,
  DispatchAnalytics,
  InventoryAnalytics,
  LeadsAnalytics,
  ServiceSplitAnalytics,
  VendorsAnalytics
} from '../models/dashboard.models';

/**
 * Thin wrapper over the `/analytics` endpoints (ACTION_PLAN E10-09, TECH_SPEC
 * §5.4), matching the `ApiService`-only-HTTP convention every other feature
 * service in this app follows (see `InventoryService`, `VendorsService`).
 *
 * All six endpoints accept the same optional query params. `vendors()` is
 * included here for contract completeness — the API surface has six
 * endpoints — but the Dashboard screen itself has no consumer for it (the
 * approved prototype's dash view-model never reads a vendors aggregate); a
 * future Vendors-analytics screen can reuse this service unchanged.
 */
@Injectable({ providedIn: 'root' })
export class DashboardAnalyticsService {
  private readonly api = inject(ApiService);

  leads(params: AnalyticsQueryParams): Observable<LeadsAnalytics> {
    return this.api.get<LeadsAnalytics>('/analytics/leads', this.toQuery(params));
  }

  serviceSplit(params: AnalyticsQueryParams): Observable<ServiceSplitAnalytics> {
    return this.api.get<ServiceSplitAnalytics>('/analytics/service-split', this.toQuery(params));
  }

  categoryMix(params: AnalyticsQueryParams): Observable<CategoryMixAnalytics> {
    return this.api.get<CategoryMixAnalytics>('/analytics/category-mix', this.toQuery(params));
  }

  vendors(params: AnalyticsQueryParams): Observable<VendorsAnalytics> {
    return this.api.get<VendorsAnalytics>('/analytics/vendors', this.toQuery(params));
  }

  inventory(params: AnalyticsQueryParams): Observable<InventoryAnalytics> {
    return this.api.get<InventoryAnalytics>('/analytics/inventory', this.toQuery(params));
  }

  dispatch(params: AnalyticsQueryParams): Observable<DispatchAnalytics> {
    return this.api.get<DispatchAnalytics>('/analytics/dispatch', this.toQuery(params));
  }

  private toQuery(params: AnalyticsQueryParams): Record<string, string | undefined> {
    return {
      fromDate: params.fromDate,
      toDate: params.toDate,
      categoryId: params.categoryId,
      serviceTypeId: params.serviceTypeId
    };
  }
}
