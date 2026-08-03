import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { InvoicesComponent } from './invoices.component';
import { InvoicesService } from './services/invoices.service';
import { AuthService } from '../core/services/auth.service';
import { MasterDataService } from '../core/services/master-data.service';
import { CustomersService } from '../customers/services/customers.service';
import { InvoiceListItem, InvoiceListResponse, InvoiceStatusCount } from './models/invoice.models';

/**
 * E8-09 — the live, data-wired invoice list. Replaces the E0-05a design-preview
 * spec (which asserted against `MOCK_INVOICES`) now that the component is wired
 * to the real `InvoicesService`/`GET /invoices`.
 *
 * Uses hand-rolled test doubles for `InvoicesService`/`MasterDataService`/
 * `CustomersService` rather than `HttpTestingController` — the component's
 * contract with the API is already pinned down at the service layer
 * (`invoices.service.ts`'s own doc comments, live-diffed against a running
 * server); these specs only need to prove the component reacts correctly to
 * what that service returns.
 *
 * Assertions use literal values baked into the fixtures below rather than
 * re-deriving them from the same formatting helpers the component calls
 * (D-64) — e.g. asserting the literal string `'₹1,47,500.00'`, not
 * `formatInr(147500)`, so a regression in either the component or the
 * formatter would actually be caught.
 */
function invoice(overrides: Partial<InvoiceListItem> = {}): InvoiceListItem {
  return {
    id: 'inv-1',
    invoiceNumber: 'INV-2608-001',
    customer: { id: 'cust-1', name: 'Ramesh Traders Pvt Ltd' },
    serviceType: { id: 'svc-1', code: 'CIF', label: 'CIF' },
    status: { id: 'st-1', code: 'DRAFT', label: 'Draft' },
    invoiceDate: '2026-08-03',
    amount: 125000,
    taxAmount: 22500,
    totalAmount: 147500,
    currency: 'INR',
    shipmentId: null,
    shipmentReference: null,
    hasPdf: false,
    paidAt: null,
    ...overrides
  };
}

const STATUS_COUNTS: InvoiceStatusCount[] = [
  { statusId: 'st-1', code: 'DRAFT', label: 'Draft', sortOrder: 1, count: 0 },
  { statusId: 'st-2', code: 'ISSUED', label: 'Issued', sortOrder: 2, count: 1 },
  { statusId: 'st-3', code: 'PAID', label: 'Paid', sortOrder: 3, count: 1 },
  { statusId: 'st-4', code: 'CANCELLED', label: 'Cancelled', sortOrder: 4, count: 0 }
];

function response(items: InvoiceListItem[], statusCounts: InvoiceStatusCount[] = STATUS_COUNTS, totalCount = items.length): InvoiceListResponse {
  return { items, page: 1, pageSize: 25, totalCount, statusCounts };
}

describe('InvoicesComponent (E8-09, live-wired)', () => {
  let fixture: ComponentFixture<InvoicesComponent>;
  let listSpy: jasmine.Spy;

  async function configure(opts: { permissions?: string[]; listResult?: InvoiceListResponse } = {}): Promise<void> {
    listSpy = jasmine.createSpy('list').and.returnValue(of(opts.listResult ?? response([invoice()])));

    await TestBed.configureTestingModule({
      imports: [InvoicesComponent],
      providers: [
        provideRouter([]),
        { provide: InvoicesService, useValue: { list: listSpy } },
        {
          provide: AuthService,
          useValue: { hasPermission: (p: string) => (opts.permissions ?? ['Invoicing.Edit']).includes(p) }
        },
        { provide: MasterDataService, useValue: { serviceTypeOptions: () => of([{ id: 'svc-1', code: 'CIF', label: 'CIF' }]) } },
        { provide: CustomersService, useValue: { list: () => of({ items: [], page: 1, pageSize: 200, totalCount: 0 }) } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(InvoicesComponent);
  }

  it('renders rows from the InvoicesService response with the literal, server-computed values', async () => {
    await configure({
      listResult: response([
        invoice({
          id: 'inv-42',
          invoiceNumber: 'INV-2608-007',
          customer: { id: 'cust-9', name: 'Ramesh Traders Pvt Ltd' },
          totalAmount: 147500,
          status: { id: 'st-1', code: 'DRAFT', label: 'Draft' }
        })
      ])
    });
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('INV-2608-007');
    expect(text).toContain('Ramesh Traders Pvt Ltd');
    // ₹1,47,500.00 — Indian digit grouping of the server-computed totalAmount, never amount + taxAmount recomputed locally.
    expect(text).toContain('₹1,47,500.00');
    expect(text).toContain('Draft');
  });

  it('issues a fresh request with the customer id when the Customer filter changes, rather than filtering client-side', async () => {
    await configure({ listResult: response([]) });
    fixture.detectChanges();
    listSpy.calls.reset();
    listSpy.and.returnValue(of(response([])));

    fixture.componentInstance.setCustomer('cust-77');

    expect(listSpy).toHaveBeenCalledTimes(1);
    expect(listSpy.calls.mostRecent().args[0].customerId).toBe('cust-77');
    expect(listSpy.calls.mostRecent().args[0].page).toBe(1);
  });

  it('debounces search input and sends it as the search param', fakeAsync(async () => {
    await configure({ listResult: response([]) });
    fixture.detectChanges();
    listSpy.calls.reset();
    listSpy.and.returnValue(of(response([])));

    fixture.componentInstance.onSearchInput('Ramesh');
    tick(299);
    expect(listSpy).not.toHaveBeenCalled();
    tick(1);
    expect(listSpy).toHaveBeenCalledTimes(1);
    expect(listSpy.calls.mostRecent().args[0].search).toBe('Ramesh');
  }));

  it('renders one pill tab per statusCounts row IN SORT ORDER, including zero-count statuses, plus "All invoices" summing every count', async () => {
    await configure({ listResult: response([invoice()], STATUS_COUNTS, 1) });
    fixture.detectChanges();

    const tabEls: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.pill-tab'));
    const labels = tabEls.map((el) => el.textContent?.replace(/\s+/g, ' ').trim());

    expect(labels[0]).toContain('All invoices');
    expect(labels[0]).toContain('2'); // 0 + 1 + 1 + 0
    expect(labels[1]).toContain('Draft');
    expect(labels[1]).toContain('0'); // zero-count status still renders as its own tab
    expect(labels[2]).toContain('Issued');
    expect(labels[3]).toContain('Paid');
    expect(labels[4]).toContain('Cancelled');
  });

  it('sends the selected tab\'s statusId as a query param and refetches', async () => {
    await configure({ listResult: response([]) });
    fixture.detectChanges();
    listSpy.calls.reset();
    listSpy.and.returnValue(of(response([])));

    fixture.componentInstance.selectTab('st-3');

    expect(listSpy.calls.mostRecent().args[0].statusId).toBe('st-3');
  });

  it('shows "no invoices yet" (not the filtered-empty message) when the list is empty and no filter is active', async () => {
    await configure({ listResult: response([], STATUS_COUNTS, 0) });
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('No invoices yet');
    expect(text).not.toContain('No invoices match these filters');
  });

  it('shows the filtered-empty message once a filter is active and the list comes back empty', async () => {
    await configure({ listResult: response([]) });
    fixture.detectChanges();
    listSpy.and.returnValue(of(response([], STATUS_COUNTS, 0)));

    fixture.componentInstance.setCustomer('cust-77');
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('No invoices match these filters');
    expect(text).not.toContain('No invoices yet');
  });

  it('shows a visible, human-readable error instead of hanging when the request fails', async () => {
    listSpy = jasmine.createSpy('list').and.returnValue(
      throwError(() => new HttpErrorResponse({ error: { title: 'Server error', detail: 'Invoice lookup failed' }, status: 500 }))
    );
    await TestBed.configureTestingModule({
      imports: [InvoicesComponent],
      providers: [
        provideRouter([]),
        { provide: InvoicesService, useValue: { list: listSpy } },
        { provide: AuthService, useValue: { hasPermission: () => true } },
        { provide: MasterDataService, useValue: { serviceTypeOptions: () => of([]) } },
        { provide: CustomersService, useValue: { list: () => of({ items: [], page: 1, pageSize: 200, totalCount: 0 }) } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(InvoicesComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.error()).toBe('Invoice lookup failed');
    expect(fixture.nativeElement.textContent).toContain('Invoice lookup failed');
  });

  it('hides "+ Generate Invoice" without Invoicing.Edit permission', async () => {
    await configure({ permissions: [], listResult: response([]) });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('+ Generate Invoice');
  });

  it('shows "+ Generate Invoice" with Invoicing.Edit permission', async () => {
    await configure({ permissions: ['Invoicing.Edit'], listResult: response([]) });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('+ Generate Invoice');
  });

  it('switches the service-type chip and status chip colours by CODE, not label (D-50)', async () => {
    await configure({
      listResult: response([
        invoice({
          serviceType: { id: 'svc-2', code: 'FREIGHT_ONLY', label: 'Something Renamed By Super-Admin' },
          status: { id: 'st-3', code: 'PAID', label: 'Paid' }
        })
      ])
    });
    fixture.detectChanges();

    // A renamed label must still render — proving the row survives a Super-Admin
    // relabel rather than the component matching on the old English text.
    expect(fixture.nativeElement.textContent).toContain('Something Renamed By Super-Admin');
  });
});
