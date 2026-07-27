import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import {
  CreateCustomerRequest,
  CreateInteractionRequest,
  CustomerDetail,
  CustomersListParams,
  CustomersListResponse,
  DueFollowUp,
  InteractionDto,
  TimelineEvent
} from '../models/customer.models';

/**
 * Thin wrapper over the `/customers` endpoints (ACTION_PLAN E4-13/14/15).
 * All HTTP goes through the shared ApiService — no component talks to
 * HttpClient directly. Every method matches the binding contract fixed by
 * the coordinator; nothing here guesses at unspecified backend behaviour.
 */
@Injectable({ providedIn: 'root' })
export class CustomersService {
  private readonly api = inject(ApiService);

  list(params: CustomersListParams): Observable<CustomersListResponse> {
    return this.api.get<CustomersListResponse>('/customers', {
      search: params.search,
      page: params.page,
      pageSize: params.pageSize,
      statusId: params.statusId,
      serviceTypeId: params.serviceTypeId,
      categoryId: params.categoryId,
      region: params.region,
      ownerUserId: params.ownerUserId,
      tag: params.tag
    });
  }

  getById(id: string): Observable<CustomerDetail> {
    return this.api.get<CustomerDetail>(`/customers/${id}`);
  }

  /**
   * POST /customers. On a duplicate phone the API returns 409 with the
   * existing customer's summary in the ProblemDetails body (E4-10) — this
   * method does not swallow that error, it's the caller's job (the intake
   * screen) to inspect it and re-call with `{ ...request, confirmDuplicate: true }`.
   */
  create(request: CreateCustomerRequest): Observable<CustomerDetail> {
    return this.api.post<CustomerDetail>('/customers', request);
  }

  update(id: string, request: CreateCustomerRequest): Observable<CustomerDetail> {
    return this.api.put<CustomerDetail>(`/customers/${id}`, request);
  }

  getTimeline(id: string): Observable<TimelineEvent[]> {
    return this.api.get<TimelineEvent[]>(`/customers/${id}/timeline`);
  }

  addInteraction(id: string, request: CreateInteractionRequest): Observable<InteractionDto> {
    return this.api.post<InteractionDto>(`/customers/${id}/interactions`, request);
  }

  setOwner(id: string, ownerUserId: string | null): Observable<CustomerDetail> {
    return this.api.put<CustomerDetail>(`/customers/${id}/owner`, { ownerUserId });
  }

  dueFollowUps(asOf: string): Observable<DueFollowUp[]> {
    return this.api.get<DueFollowUp[]>('/customers/follow-ups/due', { asOf });
  }
}
