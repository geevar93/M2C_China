import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DashboardAnalyticsService } from './dashboard-analytics.service';

describe('DashboardAnalyticsService', () => {
  let service: DashboardAnalyticsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(DashboardAnalyticsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const PARAMS = { fromDate: '2026-07-01', toDate: '2026-08-01', categoryId: 'cat-1', serviceTypeId: 'svc-1' };

  it('GETs /analytics/leads with all four query params', () => {
    service.leads(PARAMS).subscribe();
    const req = httpMock.expectOne(
      (r) =>
        r.url === '/api/v1/analytics/leads' &&
        r.params.get('fromDate') === '2026-07-01' &&
        r.params.get('toDate') === '2026-08-01' &&
        r.params.get('categoryId') === 'cat-1' &&
        r.params.get('serviceTypeId') === 'svc-1'
    );
    expect(req.request.method).toBe('GET');
    req.flush({
      series: [],
      bySource: [],
      byStatus: [],
      totalLeads: 0,
      wonCount: 0,
      conversionRate: 0,
      currentPeriodCount: 0,
      priorPeriodCount: 0
    });
  });

  it('GETs /analytics/service-split', () => {
    service.serviceSplit(PARAMS).subscribe();
    const req = httpMock.expectOne((r) => r.url === '/api/v1/analytics/service-split');
    expect(req.request.method).toBe('GET');
    req.flush({ totalCustomers: 0, items: [] });
  });

  it('GETs /analytics/category-mix', () => {
    service.categoryMix(PARAMS).subscribe();
    const req = httpMock.expectOne((r) => r.url === '/api/v1/analytics/category-mix');
    expect(req.request.method).toBe('GET');
    req.flush({ items: [] });
  });

  it('GETs /analytics/vendors', () => {
    service.vendors(PARAMS).subscribe();
    const req = httpMock.expectOne((r) => r.url === '/api/v1/analytics/vendors');
    expect(req.request.method).toBe('GET');
    req.flush({ totalActiveVendors: 0, byCategory: [] });
  });

  it('GETs /analytics/inventory', () => {
    service.inventory(PARAMS).subscribe();
    const req = httpMock.expectOne((r) => r.url === '/api/v1/analytics/inventory');
    expect(req.request.method).toBe('GET');
    req.flush({ onHandValue: 0, byCategory: [], shipmentsByStatus: [], inTransitCount: 0, pastEtaCount: 0, belowReorderCount: 0 });
  });

  it('GETs /analytics/dispatch', () => {
    service.dispatch(PARAMS).subscribe();
    const req = httpMock.expectOne((r) => r.url === '/api/v1/analytics/dispatch');
    expect(req.request.method).toBe('GET');
    req.flush({ series: [], byStaff: [], byKind: [], totalDispatches: 0 });
  });

  it('omits undefined params rather than sending them as the literal string "undefined"', () => {
    service.leads({ fromDate: '2026-07-01' }).subscribe();
    const req = httpMock.expectOne(
      (r) =>
        r.url === '/api/v1/analytics/leads' &&
        r.params.get('fromDate') === '2026-07-01' &&
        !r.params.has('toDate') &&
        !r.params.has('categoryId') &&
        !r.params.has('serviceTypeId')
    );
    req.flush({
      series: [],
      bySource: [],
      byStatus: [],
      totalLeads: 0,
      wonCount: 0,
      conversionRate: 0,
      currentPeriodCount: 0,
      priorPeriodCount: 0
    });
  });
});
