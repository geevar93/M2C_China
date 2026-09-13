import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { CompanySettings, UpsertCompanySettingsRequest } from '../../invoices/models/invoice.models';

/**
 * Thin wrapper over the existing `/admin/company-settings` endpoints
 * (`AdminCompanySettingsController` — no backend change). Both routes are gated by
 * `Admin.ManageMasterData`, including the read: billing and bank details are
 * sensitive in a way the master-data lookup labels are not.
 *
 * Follows `AdminMasterDataService`'s convention — all HTTP through the shared
 * `ApiService`, never `HttpClient` directly.
 */
@Injectable({ providedIn: 'root' })
export class AdminCompanySettingsService {
  private readonly api = inject(ApiService);

  get(): Observable<CompanySettings> {
    return this.api.get<CompanySettings>('/admin/company-settings');
  }

  /** Upserts the singleton and returns the saved row. */
  upsert(request: UpsertCompanySettingsRequest): Observable<CompanySettings> {
    return this.api.put<CompanySettings>('/admin/company-settings', request);
  }
}
