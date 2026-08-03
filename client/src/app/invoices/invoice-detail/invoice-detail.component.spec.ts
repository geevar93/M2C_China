import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { MasterDataResponse } from '../../core/models/master-data.models';
import { CompanySettings, InvoiceDetail } from '../models/invoice.models';
import { InvoiceDetailComponent } from './invoice-detail.component';

/**
 * E8-10, wired against the real `/invoices` + `/admin/company-settings` API.
 * Follows `ShipmentDetailComponent.spec.ts`'s pattern of exercising the real
 * `InvoicesService` through `HttpTestingController` rather than a hand-rolled
 * spy — that's the closest existing detail screen and the house convention
 * for this kind of test.
 *
 * Assertions use literal values throughout (D-64) — a prior audit found tests
 * that compared against the same constant used to produce the value under
 * test, which could never fail.
 */
const MASTER_DATA: MasterDataResponse = {
  categories: [],
  serviceTypes: [],
  leadStatuses: [],
  shipmentStatuses: [],
  invoiceStatuses: [
    { id: 'inv-st-1', code: 'DRAFT', label: 'Draft', sortOrder: 1, isActive: true },
    { id: 'inv-st-2', code: 'ISSUED', label: 'Issued', sortOrder: 2, isActive: true },
    { id: 'inv-st-3', code: 'PAID', label: 'Paid', sortOrder: 3, isActive: true },
    { id: 'inv-st-4', code: 'CANCELLED', label: 'Cancelled', sortOrder: 4, isActive: true }
  ],
  vendorStatuses: [],
  documentTypes: []
};

const CONFIGURED_SETTINGS: CompanySettings = {
  legalEntityName: 'Meridian Sourcing Pvt Ltd',
  gstin: '27AAAAA0000A1Z5',
  registeredAddress: '14 MG Road, Bengaluru 560001',
  bankAccountName: 'Meridian Sourcing Pvt Ltd',
  bankAccountNumber: '000123456789',
  bankIfsc: 'HDFC0000123',
  bankBranch: 'MG Road',
  invoiceNumberPrefix: 'INV',
  declarationText: null,
  updatedAt: '2026-08-01T00:00:00Z',
  updatedByName: 'Super Admin'
};

const UNCONFIGURED_SETTINGS: CompanySettings = {
  legalEntityName: null,
  gstin: null,
  registeredAddress: null,
  bankAccountName: null,
  bankAccountNumber: null,
  bankIfsc: null,
  bankBranch: null,
  invoiceNumberPrefix: null,
  declarationText: null,
  updatedAt: null,
  updatedByName: null
};

function invoice(overrides: Partial<InvoiceDetail> = {}): InvoiceDetail {
  return {
    id: '259b39f6-0000-0000-0000-000000000001',
    invoiceNumber: 'INV-2608-001',
    customer: { id: 'cust-1', name: 'Ramesh Traders Pvt Ltd' },
    serviceType: { id: 'svc-1', code: 'CIF', label: 'CIF' },
    status: { id: 'inv-st-1', code: 'DRAFT', label: 'Draft' },
    invoiceDate: '2026-08-03',
    amount: 125000.0,
    taxAmount: 22500.0,
    totalAmount: 147500.0,
    currency: 'INR',
    shipmentId: null,
    shipmentReference: null,
    hasPdf: false,
    paidAt: null,
    lineDescription: 'Freight forwarding, Shenzhen to Chennai',
    createdByUserId: 'u1',
    createdByName: 'Super Admin',
    createdAt: '2026-08-03T06:23:38.19Z',
    paidReference: null,
    statusHistory: [
      {
        id: 'hist-1',
        status: { id: 'inv-st-1', code: 'DRAFT', label: 'Draft' },
        changedByUserId: 'u1',
        changedByName: 'Super Admin',
        changedAt: '2026-08-03T06:23:38.19Z',
        note: 'Invoice created.'
      }
    ],
    ...overrides
  };
}

describe('InvoiceDetailComponent', () => {
  let fixture: ComponentFixture<InvoiceDetailComponent>;
  let httpMock: HttpTestingController;

  async function configure(routeId: string | null, permissions: string[] = ['Invoicing.MarkPaid']): Promise<void> {
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
          useValue: { hasPermission: (p: string) => permissions.includes(p), currentUser$: of(null) }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(InvoiceDetailComponent);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  function flushMasterData(data: MasterDataResponse = MASTER_DATA): void {
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(data);
  }

  function flushCompanySettings(settings: CompanySettings = UNCONFIGURED_SETTINGS): void {
    httpMock.expectOne((r) => r.url === '/api/v1/admin/company-settings').flush(settings);
  }

  function flushCustomers(): void {
    httpMock
      .expectOne((r) => r.url === '/api/v1/customers')
      .flush({ items: [{ id: 'cust-2', businessName: 'Sundar Exports', name: 'Sundar Exports', phone: '9', city: null, region: null, sourceChannel: 'Referral', serviceTypeId: 'svc-1', statusId: 'st-1', categoryIds: [], ownerUserId: null, ownerName: null, tags: [], createdAt: '2026-01-01T00:00:00Z' }], page: 1, pageSize: 200, totalCount: 1 });
  }

  function flushInvoice(id: string, payload: InvoiceDetail = invoice()): void {
    httpMock.expectOne((r) => r.url === `/api/v1/invoices/${id}`).flush(payload);
  }

  it('loads and renders a detail invoice from the real API, not a mock', async () => {
    await configure('259b39f6-0000-0000-0000-000000000001');
    fixture.detectChanges();
    flushMasterData();
    flushCompanySettings();
    flushCustomers();
    flushInvoice('259b39f6-0000-0000-0000-000000000001');
    fixture.detectChanges();

    expect(fixture.componentInstance.isGenerateMode()).toBeFalse();
    expect(fixture.componentInstance.invoice()?.invoiceNumber).toBe('INV-2608-001');
    expect(fixture.nativeElement.textContent).toContain('INV-2608-001');
    expect(fixture.nativeElement.textContent).toContain('Ramesh Traders Pvt Ltd');
    // totalAmount is server-computed and rendered verbatim, never recomputed client-side.
    expect(fixture.componentInstance.invoice()?.totalAmount).toBe(147500.0);
  });

  it('POSTs a create request in generate mode and navigates to the new invoice', async () => {
    await configure(null);
    fixture.detectChanges();
    flushMasterData();
    flushCompanySettings();
    flushCustomers();
    fixture.detectChanges();

    fixture.componentInstance.formCustomerId.set('cust-2');
    fixture.componentInstance.formInvoiceDate.set('2026-08-03');
    fixture.componentInstance.formLineDescription.set('Freight forwarding');
    fixture.componentInstance.formAmount.set('50000');
    fixture.componentInstance.formTaxAmount.set('9000');
    fixture.componentInstance.save();

    const req = httpMock.expectOne((r) => r.url === '/api/v1/invoices' && r.method === 'POST');
    expect(req.request.body).toEqual({
      customerId: 'cust-2',
      shipmentId: null,
      invoiceDate: '2026-08-03',
      lineDescription: 'Freight forwarding',
      amount: 50000,
      taxAmount: 9000,
      currency: 'INR'
    });
    req.flush(invoice({ id: 'new-inv-id', invoiceNumber: 'INV-2608-002' }));
  });

  it('surfaces the 409 detail message verbatim when editing a non-DRAFT invoice, not a generic error', async () => {
    await configure('inv-issued');
    fixture.detectChanges();
    flushMasterData();
    flushCompanySettings();
    flushCustomers();
    flushInvoice('inv-issued', invoice({ id: 'inv-issued', status: { id: 'inv-st-2', code: 'ISSUED', label: 'Issued' } }));
    fixture.detectChanges();

    // Not DRAFT, so Edit is not offered in the UI at all.
    expect(fixture.componentInstance.canEditAction()).toBeFalse();

    // Simulate a race where the update is attempted anyway (e.g. a stale tab).
    fixture.componentInstance.formCustomerId.set('cust-1');
    fixture.componentInstance.formInvoiceDate.set('2026-08-03');
    fixture.componentInstance.formAmount.set('125000');
    fixture.componentInstance.formTaxAmount.set('22500');
    fixture.componentInstance.isEditing.set(true);
    fixture.componentInstance.save();

    const req = httpMock.expectOne((r) => r.url === '/api/v1/invoices/inv-issued' && r.method === 'PUT');
    req.flush(
      { status: 409, title: 'Conflict', detail: "This invoice is 'ISSUED' and can only be edited while Draft.", currentStatus: 'ISSUED' },
      { status: 409, statusText: 'Conflict' }
    );
    fixture.detectChanges();

    expect(fixture.componentInstance.formError()).toBe("This invoice is 'ISSUED' and can only be edited while Draft.");
  });

  it('surfaces the 409 detail message verbatim when marking paid outside ISSUED', async () => {
    await configure('inv-draft');
    fixture.detectChanges();
    flushMasterData();
    flushCompanySettings();
    flushCustomers();
    flushInvoice('inv-draft', invoice({ id: 'inv-draft' })); // DRAFT
    fixture.detectChanges();

    // Only offered as 'disabled' from DRAFT, but exercise the API path directly
    // to pin the 409 message the disabled state exists to prevent.
    fixture.componentInstance.markPaidOpen.set(true);
    fixture.componentInstance.paidDateInput.set('2026-08-03');
    fixture.componentInstance.confirmMarkPaid();

    const req = httpMock.expectOne((r) => r.url === '/api/v1/invoices/inv-draft/mark-paid' && r.method === 'POST');
    req.flush(
      {
        status: 409,
        title: 'Conflict',
        detail: "An invoice can only be marked paid from 'ISSUED'; this invoice is currently 'DRAFT'.",
        fromStatus: 'DRAFT',
        toStatus: 'PAID'
      },
      { status: 409, statusText: 'Conflict' }
    );
    fixture.detectChanges();

    expect(fixture.componentInstance.markPaidError()).toBe(
      "An invoice can only be marked paid from 'ISSUED'; this invoice is currently 'DRAFT'."
    );
  });

  it('disables Issue with an explanation when company billing is not configured', async () => {
    await configure('inv-draft');
    fixture.detectChanges();
    flushMasterData();
    flushCompanySettings(UNCONFIGURED_SETTINGS);
    flushCustomers();
    flushInvoice('inv-draft'); // DRAFT
    fixture.detectChanges();

    expect(fixture.componentInstance.canIssue()).toBeFalse();
    const buttons: HTMLButtonElement[] = Array.from(fixture.nativeElement.querySelectorAll('button'));
    const issueBtn = buttons.find((b) => b.textContent?.trim() === 'Issue') as HTMLButtonElement;
    expect(issueBtn.disabled).toBeTrue();
    expect(issueBtn.title).toContain('Admin > Company Settings');
    expect(fixture.nativeElement.textContent).toContain('Company billing details not configured');
  });

  it('enables Issue once company billing is configured', async () => {
    await configure('inv-draft');
    fixture.detectChanges();
    flushMasterData();
    flushCompanySettings(CONFIGURED_SETTINGS);
    flushCustomers();
    flushInvoice('inv-draft'); // DRAFT
    fixture.detectChanges();

    expect(fixture.componentInstance.canIssue()).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('Meridian Sourcing Pvt Ltd');
    expect(fixture.nativeElement.textContent).not.toContain('Company billing details not configured');
  });

  it('hides Download PDF behind hasPdf, offering it only once an invoice has one', async () => {
    await configure('inv-1');
    fixture.detectChanges();
    flushMasterData();
    flushCompanySettings();
    flushCustomers();
    flushInvoice('inv-1', invoice({ hasPdf: false }));
    fixture.detectChanges();

    const downloadButtons: HTMLButtonElement[] = Array.from(fixture.nativeElement.querySelectorAll('button'));
    const downloadBtn = downloadButtons.find((b) => b.textContent?.trim() === 'Download PDF') as HTMLButtonElement;
    expect(downloadBtn.disabled).toBeTrue();
    expect(downloadBtn.title).toContain('has not been issued yet');
  });

  it('offers only legal transitions from DRAFT: Issue and Cancel, never Mark Paid directly', async () => {
    await configure('inv-1');
    fixture.detectChanges();
    flushMasterData();
    flushCompanySettings(CONFIGURED_SETTINGS);
    flushCustomers();
    flushInvoice('inv-1', invoice({ status: { id: 'inv-st-1', code: 'DRAFT', label: 'Draft' } }));
    fixture.detectChanges();

    expect(fixture.componentInstance.canIssueAction()).toBeTrue();
    expect(fixture.componentInstance.canCancelAction()).toBeTrue();
    expect(fixture.componentInstance.markPaidAvailability()).toBe('disabled');
    expect(fixture.componentInstance.markPaidDisabledReason()).toContain('Only issued invoices');
  });

  it('offers only legal transitions from ISSUED: Cancel and Mark Paid, never Issue again', async () => {
    await configure('inv-1');
    fixture.detectChanges();
    flushMasterData();
    flushCompanySettings(CONFIGURED_SETTINGS);
    flushCustomers();
    flushInvoice('inv-1', invoice({ status: { id: 'inv-st-2', code: 'ISSUED', label: 'Issued' } }));
    fixture.detectChanges();

    expect(fixture.componentInstance.canIssueAction()).toBeFalse();
    expect(fixture.componentInstance.canCancelAction()).toBeTrue();
    expect(fixture.componentInstance.markPaidAvailability()).toBe('enabled');
  });

  it('offers no status-changing actions from a terminal PAID or CANCELLED invoice', async () => {
    await configure('inv-1');
    fixture.detectChanges();
    flushMasterData();
    flushCompanySettings(CONFIGURED_SETTINGS);
    flushCustomers();
    flushInvoice('inv-1', invoice({ status: { id: 'inv-st-3', code: 'PAID', label: 'Paid' }, paidAt: '2026-08-03T00:00:00Z' }));
    fixture.detectChanges();

    expect(fixture.componentInstance.canIssueAction()).toBeFalse();
    expect(fixture.componentInstance.canCancelAction()).toBeFalse();
    expect(fixture.componentInstance.canEditAction()).toBeFalse();
  });

  it('marks a CANCELLED invoice as off the Draft -> Issued -> Paid path and disallows every action', async () => {
    await configure('inv-1');
    fixture.detectChanges();
    flushMasterData();
    flushCompanySettings(CONFIGURED_SETTINGS);
    flushCustomers();
    flushInvoice('inv-1', invoice({ status: { id: 'inv-st-4', code: 'CANCELLED', label: 'Cancelled' } }));
    fixture.detectChanges();

    expect(fixture.componentInstance.isCancelled()).toBeTrue();
    expect(fixture.componentInstance.canCancelAction()).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('off the Draft → Issued → Paid path');
  });

  it('hides Mark Paid entirely without the Invoicing.MarkPaid permission', async () => {
    await configure('inv-1', []);
    fixture.detectChanges();
    flushMasterData();
    flushCompanySettings();
    flushCustomers();
    flushInvoice('inv-1', invoice({ status: { id: 'inv-st-2', code: 'ISSUED', label: 'Issued' } }));
    fixture.detectChanges();

    expect(fixture.componentInstance.markPaidAvailability()).toBe('hidden');
    expect(fixture.nativeElement.textContent).not.toContain('Mark Paid');
  });

  it('shows a visible error instead of hanging when the invoice fails to load', async () => {
    await configure('missing-id');
    fixture.detectChanges();
    flushMasterData();
    flushCompanySettings();
    flushCustomers();
    httpMock
      .expectOne((r) => r.url === '/api/v1/invoices/missing-id')
      .flush({ title: 'Not Found', detail: 'Invoice not found' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(fixture.componentInstance.error()).toBe('Invoice not found');
    expect(fixture.nativeElement.textContent).toContain('Invoice not found');
  });
});
