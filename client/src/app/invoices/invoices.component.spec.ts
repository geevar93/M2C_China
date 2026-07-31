import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { InvoicesComponent } from './invoices.component';

/**
 * E0-05a. These screens are a DESIGN PREVIEW on mocked invoice data — there is
 * no invoicing backend (ACTION_PLAN §12.3). What these specs pin down is the
 * boundary between what is mocked and what is genuinely live: the invoices
 * themselves are mock constants, but the status/service-type filter options
 * must come from the real MasterDataService over HTTP, never a hard-coded
 * array (DR-6). A regression there would silently break the "change it
 * without a deploy" promise, which is exactly what DR-6 exists to catch.
 */
const AGGREGATE = {
  categories: [],
  serviceTypes: [
    { id: 'svc-1', code: 'CIF', label: 'CIF', sortOrder: 1, isActive: true, isSystemDefault: true },
    { id: 'svc-2', code: 'FREIGHT_ONLY', label: 'Freight Only', sortOrder: 2, isActive: true, isSystemDefault: true }
  ],
  leadStatuses: [],
  shipmentStatuses: [],
  invoiceStatuses: [
    { id: 'inv-st-1', code: 'DRAFT', label: 'Draft', sortOrder: 1, isActive: true, isSystemDefault: true },
    { id: 'inv-st-2', code: 'ISSUED', label: 'Issued', sortOrder: 2, isActive: true, isSystemDefault: true },
    { id: 'inv-st-3', code: 'PAID', label: 'Paid', sortOrder: 3, isActive: true, isSystemDefault: true },
    { id: 'inv-st-4', code: 'CANCELLED', label: 'Cancelled', sortOrder: 4, isActive: true, isSystemDefault: true }
  ],
  vendorStatuses: []
};

describe('InvoicesComponent (E0-05a design preview)', () => {
  let fixture: ComponentFixture<InvoicesComponent>;
  let component: InvoicesComponent;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InvoicesComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(InvoicesComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function flushAggregate(): void {
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(AGGREGATE);
    fixture.detectChanges();
  }

  it('shows the design-preview banner, so a demo cannot mistake it for a working module', () => {
    fixture.detectChanges();
    flushAggregate();

    const banner: HTMLElement | null = fixture.nativeElement.querySelector('.banner-warning');
    expect(banner).toBeTruthy();
    expect(banner!.textContent).toContain('Design preview on sample data');
    expect(banner!.textContent).toContain('no invoicing backend');
  });

  it('resolves status and service-type filter options from MasterDataService, not a hard-coded list (DR-6)', () => {
    fixture.detectChanges();
    flushAggregate();

    expect(component.invoiceStatusOptions().map((o) => o.code)).toEqual(['DRAFT', 'ISSUED', 'PAID', 'CANCELLED']);
    // The label comes from the server row, not from the mock invoice data —
    // proving the screen would follow a renamed status without a redeploy.
    expect(component.invoiceStatusOptions().map((o) => o.label)).toContain('Issued');
    expect(component.serviceTypeOptions().map((o) => o.code)).toEqual(['CIF', 'FREIGHT_ONLY']);
  });

  it('renders the full sample set before any filter is applied', () => {
    fixture.detectChanges();
    flushAggregate();

    expect(component.rows().length).toBe(8);
    expect(component.noResults()).toBeFalse();
  });

  it('narrows rows by search across invoice number and customer', () => {
    fixture.detectChanges();
    flushAggregate();

    component.search.set('Meena');
    expect(component.rows().length).toBe(1);

    component.search.set('PREVIEW-0004');
    expect(component.rows().length).toBe(1);
  });

  it('narrows rows by status, resolving the filter through the real status id', () => {
    fixture.detectChanges();
    flushAggregate();

    component.setStatus('inv-st-3'); // PAID
    expect(component.rows().length).toBe(2);
    expect(component.filteredInvoices().every((i) => i.statusCode === 'PAID')).toBeTrue();
  });

  it('narrows rows by service type', () => {
    fixture.detectChanges();
    flushAggregate();

    component.setServiceType('svc-2'); // Freight-only: inv-0002, 0004, 0007, 0008
    expect(component.rows().length).toBe(4);
    expect(component.filteredInvoices().every((i) => i.serviceTypeCode === 'FREIGHT_ONLY')).toBeTrue();
  });

  it('surfaces an explicit empty state rather than a blank table when filters match nothing', () => {
    fixture.detectChanges();
    flushAggregate();

    component.search.set('no-such-invoice-anywhere');
    fixture.detectChanges();

    expect(component.rows().length).toBe(0);
    expect(component.noResults()).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('No invoices match these filters');
  });

  it('clearFilters restores the full sample set', () => {
    fixture.detectChanges();
    flushAggregate();

    component.search.set('Meena');
    component.setStatus('inv-st-3');
    expect(component.rows().length).toBeLessThan(8);

    component.clearFilters();
    expect(component.rows().length).toBe(8);
  });

  it('handles a master-data load failure with a retry affordance, not an indefinite spinner', () => {
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(
      { title: 'Service unavailable' },
      { status: 503, statusText: 'Service Unavailable' }
    );
    fixture.detectChanges();

    expect(component.masterDataError()).toBeTruthy();
    expect(component.masterDataLoading()).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('Retry');
  });
});
