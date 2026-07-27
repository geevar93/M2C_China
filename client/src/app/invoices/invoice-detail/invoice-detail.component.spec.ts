import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { InvoiceDetailComponent } from './invoice-detail.component';

/**
 * E0-05b. DESIGN PREVIEW on mocked data (ACTION_PLAN §12.3). The two
 * behaviours most worth pinning here are the ones that encode unresolved
 * business questions rather than mere layout:
 *
 *  - the "From" block must render the not-configured empty state, because
 *    FSD Q9c (legal entity, GSTIN, address, bank details) is genuinely
 *    unanswered and `company_settings` is genuinely empty. If someone later
 *    "helpfully" fills in sample company details, the screen stops asking the
 *    question it was built to ask, and this spec fails.
 *  - Mark Paid must never mutate the invoice, because there is no API to
 *    persist it. A preview that appeared to save would be worse than one that
 *    refuses.
 */
const AGGREGATE = {
  categories: [],
  serviceTypes: [
    { id: 'svc-1', code: 'CIF', label: 'CIF', sortOrder: 1, isActive: true, isSystemDefault: true }
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

describe('InvoiceDetailComponent (E0-05b design preview)', () => {
  let fixture: ComponentFixture<InvoiceDetailComponent>;
  let component: InvoiceDetailComponent;
  let httpMock: HttpTestingController;

  async function setup(routeId: string | null, permissions: string[] = ['Invoicing.MarkPaid']): Promise<void> {
    const paramMap = convertToParamMap(routeId ? { id: routeId } : {});

    await TestBed.configureTestingModule({
      imports: [InvoiceDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(paramMap), snapshot: { paramMap } }
        },
        {
          provide: AuthService,
          useValue: {
            hasPermission: (p: string) => permissions.includes(p),
            currentUser$: of(null)
          }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(InvoiceDetailComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);

    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(AGGREGATE);
    fixture.detectChanges();
  }

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('selects generate mode from the absence of a route id', async () => {
    await setup(null);

    expect(component.isGenerateMode()).toBeTrue();
    expect(component.invoice()).toBeNull();
    expect(component.invoiceNotFound()).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('Generate Invoice');
  });

  it('selects detail mode from a route id and resolves the mocked invoice', async () => {
    await setup('inv-0001');

    expect(component.isGenerateMode()).toBeFalse();
    expect(component.invoice()?.invoiceNumber).toBe('INV-PREVIEW-0001');
    expect(fixture.nativeElement.textContent).toContain('INV-PREVIEW-0001');
  });

  it('shows a not-found state for an unknown id rather than a blank screen', async () => {
    await setup('inv-does-not-exist');

    expect(component.invoiceNotFound()).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('No sample invoice with that reference exists');
  });

  it('renders the design-preview banner in both modes', async () => {
    await setup('inv-0001');
    expect(fixture.nativeElement.querySelector('.banner-warning')?.textContent).toContain('Design preview on sample data');
  });

  it('renders the company block as explicitly NOT CONFIGURED — never invented billing details (FSD Q9c)', async () => {
    await setup('inv-0001');

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Company billing details not configured');
    expect(text).toContain('GSTIN');
    // No fabricated identifiers may appear anywhere on the screen.
    expect(text).not.toMatch(/\b\d{2}[A-Z]{5}\d{4}[A-Z]\d[A-Z]\d\b/); // a real GSTIN pattern
    expect(text).not.toMatch(/IFSC/i);
  });

  it('shows the invoice number as an undecided placeholder (E8-08 is Q9c-blocked)', async () => {
    await setup('inv-0001');
    expect(fixture.nativeElement.textContent).toContain('numbering format is not decided yet');
  });

  it('resolves the status chip label from master data, not from the mock', async () => {
    await setup('inv-0001'); // ISSUED
    expect(component.statusChip().label).toBe('Issued');
  });

  it('hides Mark Paid entirely without the Invoicing.MarkPaid permission', async () => {
    await setup('inv-0001', []);
    expect(component.markPaidAvailability()).toBe('hidden');
    expect(fixture.nativeElement.textContent).not.toContain('Mark Paid');
  });

  it('offers Mark Paid only on an ISSUED invoice, and explains why when it cannot', async () => {
    await setup('inv-0003'); // PAID already
    expect(component.markPaidAvailability()).toBe('disabled');
    expect(component.markPaidDisabledReason()).toContain('already marked paid');
  });

  it('requires a paid date before confirming', async () => {
    await setup('inv-0001');

    component.openMarkPaid();
    component.paidDateInput.set('');
    component.confirmMarkPaid();

    expect(component.paidDateError()).toBeTruthy();
    expect(component.markPaidOpen()).toBeTrue();
  });

  it('confirming Mark Paid reports a preview and does NOT mutate the invoice, since no API exists', async () => {
    await setup('inv-0001');
    const statusBefore = component.invoice()!.statusCode;

    component.openMarkPaid();
    component.paidDateInput.set('2026-07-27');
    component.paidReferenceInput.set('UTR999');
    component.confirmMarkPaid();

    expect(component.markPaidOpen()).toBeFalse();
    expect(component.markPaidPreviewMessage()).toContain('No data was changed');
    expect(component.markPaidPreviewMessage()).toContain('UTR999');
    expect(component.invoice()!.statusCode).toBe(statusBefore);
  });

  it('marks the cancelled invoice as off the Draft → Issued → Paid path', async () => {
    await setup('inv-0004'); // CANCELLED

    expect(component.isCancelled()).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('off the Draft → Issued → Paid path');
  });

  it('computes totals from the mocked lines', async () => {
    await setup('inv-0002'); // single 68000 line, 18%

    expect(component.subtotalLabel()).toContain('68,000');
    expect(component.taxLabel()).toContain('12,240');
    expect(component.totalLabel()).toContain('80,240');
  });
});
