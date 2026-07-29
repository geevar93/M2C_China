import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DispatchDialogComponent } from './dispatch-dialog.component';
import { MasterDataResponse } from '../../core/models/master-data.models';
import { CatalogSection } from '../../catalogs/models/catalog.models';
import { CustomerListItem } from '../../customers/models/customer.models';

const MASTER_DATA: MasterDataResponse = {
  categories: [],
  serviceTypes: [{ id: 'svc-cif', code: 'CIF', label: 'CIF', sortOrder: 1, isActive: true }],
  leadStatuses: [],
  shipmentStatuses: [],
  invoiceStatuses: [],
  vendorStatuses: []
};

function customer(overrides: Partial<CustomerListItem> = {}): CustomerListItem {
  return {
    id: 'cust-1',
    name: 'Meena Shah',
    businessName: 'Meena Traders',
    phone: '+91 98250 41122',
    city: 'Surat',
    region: null,
    sourceChannel: 'WhatsApp',
    serviceTypeId: 'svc-cif',
    statusId: 'lead-active',
    categoryIds: [],
    ownerUserId: null,
    ownerName: null,
    tags: [],
    createdAt: '2026-02-11T00:00:00Z',
    ...overrides
  };
}

function section(overrides: Partial<CatalogSection> = {}): CatalogSection {
  return {
    id: 'sec-1',
    vendorId: 'ven-1',
    vendorName: 'Yiwu Jewel Craft Co.',
    title: 'Yiwu Jewel Craft — Jewellery — AW26 Catalog',
    category: { id: 'cat-jewellery', name: 'Jewellery' },
    tags: [],
    createdAt: '2026-04-01T00:00:00Z',
    documents: [
      {
        id: 'doc-1',
        catalogSectionId: 'sec-1',
        originalFilename: 'yiwu-jewel-craft-aw26-v3.pdf',
        sizeBytes: 8800000,
        versionLabel: 'v3',
        isLatest: true,
        uploadedByUserId: 'user-1',
        uploadedByName: 'Priya S.',
        uploadedAt: '2026-07-14T00:00:00Z'
      }
    ],
    ...overrides
  };
}

describe('DispatchDialogComponent', () => {
  let fixture: ComponentFixture<DispatchDialogComponent>;
  let httpMock: HttpTestingController;

  async function configure(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [DispatchDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    fixture = TestBed.createComponent(DispatchDialogComponent);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  function flushMasterData(): void {
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(MASTER_DATA);
  }

  describe('entered from a customer (customerLock set, document picked)', () => {
    it('loads catalog documents, defaults to the first one, and composes the message', async () => {
      await configure();
      fixture.componentInstance.customerLock = {
        id: 'cust-1',
        businessName: 'Meena Traders',
        subline: 'Meena Shah · +91 98250 41122',
        serviceTypeCode: 'CIF'
      };
      fixture.detectChanges();
      flushMasterData();

      httpMock.expectOne((r) => r.url === '/api/v1/catalog-sections').flush({
        items: [section()],
        page: 1,
        pageSize: 100,
        totalCount: 1
      });
      fixture.detectChanges();

      expect(fixture.componentInstance.selectedDocumentId()).toBe('doc-1');

      const composeReq = httpMock.expectOne((r) => r.url === '/api/v1/dispatch-log/compose');
      expect(composeReq.request.params.get('customerId')).toBe('cust-1');
      expect(composeReq.request.params.get('catalogDocumentId')).toBe('doc-1');
      composeReq.flush({ message: 'Hi Meena, sharing the AW26 catalog.', deepLinkUrl: 'https://wa.me/919825041122?text=hi' });
      fixture.detectChanges();

      expect(fixture.componentInstance.message()).toBe('Hi Meena, sharing the AW26 catalog.');
      expect(fixture.nativeElement.textContent).toContain('Meena Traders');
      expect(fixture.nativeElement.textContent).toContain('yiwu-jewel-craft-aw26-v3.pdf');
    });

    it('does not render a recipient dropdown when the customer is locked', async () => {
      await configure();
      fixture.componentInstance.customerLock = {
        id: 'cust-1',
        businessName: 'Meena Traders',
        subline: 'Meena Shah · +91 98250 41122',
        serviceTypeCode: 'CIF'
      };
      fixture.detectChanges();
      flushMasterData();
      httpMock.expectOne((r) => r.url === '/api/v1/catalog-sections').flush({ items: [section()], page: 1, pageSize: 100, totalCount: 1 });
      fixture.detectChanges();
      httpMock.expectOne((r) => r.url === '/api/v1/dispatch-log/compose').flush({ message: 'msg', deepLinkUrl: 'https://wa.me/1?text=x' });
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('[aria-label="Recipient"]')).toBeNull();
      expect(fixture.nativeElement.querySelector('[aria-label="Catalog PDF"]')).toBeTruthy();
    });
  });

  describe('entered from a catalog document (documentLock set, customer picked)', () => {
    it('loads customers, defaults to the first one, and composes the message', async () => {
      await configure();
      fixture.componentInstance.documentLock = {
        documentId: 'doc-1',
        title: 'Yiwu Jewel Craft — Jewellery — AW26 Catalog',
        filename: 'yiwu-jewel-craft-aw26-v3.pdf',
        meta: 'v3 · 8.4 MB · Yiwu Jewel Craft Co.'
      };
      fixture.detectChanges();
      flushMasterData();

      httpMock.expectOne((r) => r.url === '/api/v1/customers').flush({
        items: [customer()],
        page: 1,
        pageSize: 200,
        totalCount: 1
      });
      fixture.detectChanges();

      expect(fixture.componentInstance.selectedCustomerId()).toBe('cust-1');

      const composeReq = httpMock.expectOne((r) => r.url === '/api/v1/dispatch-log/compose');
      expect(composeReq.request.params.get('customerId')).toBe('cust-1');
      expect(composeReq.request.params.get('catalogDocumentId')).toBe('doc-1');
      composeReq.flush({ message: 'Hi Meena, sharing the AW26 catalog.', deepLinkUrl: 'https://wa.me/919825041122?text=hi' });
      fixture.detectChanges();

      const text: string = fixture.nativeElement.textContent;
      expect(text).toContain('Meena Traders');
      expect(text).toContain('CIF');
      expect(fixture.nativeElement.querySelector('[aria-label="Catalog PDF"]')).toBeNull();
    });
  });

  describe('step actions', () => {
    async function configureLocked(): Promise<void> {
      await configure();
      fixture.componentInstance.customerLock = {
        id: 'cust-1',
        businessName: 'Meena Traders',
        subline: 'Meena Shah · +91 98250 41122',
        serviceTypeCode: 'CIF'
      };
      fixture.componentInstance.documentLock = {
        documentId: 'doc-1',
        title: 'AW26 Catalog',
        filename: 'yiwu-jewel-craft-aw26-v3.pdf',
        meta: 'v3 · 8.4 MB · Yiwu Jewel Craft Co.'
      };
      fixture.detectChanges();
      flushMasterData();
      httpMock.expectOne((r) => r.url === '/api/v1/dispatch-log/compose').flush({
        message: 'Hi Meena, sharing the AW26 catalog.',
        deepLinkUrl: 'https://wa.me/919825041122?text=hi'
      });
      fixture.detectChanges();
    }

    it('downloads the PDF through the authenticated endpoint and marks step 1 done', async () => {
      await configureLocked();
      spyOn(URL, 'createObjectURL').and.returnValue('blob:mock-url');
      spyOn(URL, 'revokeObjectURL');

      fixture.componentInstance.downloadStep();
      const req = httpMock.expectOne((r) => r.url === '/api/v1/catalog-documents/doc-1/download');
      expect(req.request.responseType).toBe('blob');
      req.flush(new Blob(['%PDF-1.4'], { type: 'application/pdf' }));

      expect(fixture.componentInstance.doneSteps()[1]).toBeTrue();
    });

    it('opens the server-provided deep link in a new tab and marks step 2 done — never builds the URL itself', () => {
      const openSpy = spyOn(window, 'open').and.stub();
      return configureLocked().then(() => {
        fixture.componentInstance.openWhatsAppStep();
        expect(openSpy).toHaveBeenCalledWith('https://wa.me/919825041122?text=hi', '_blank');
        expect(fixture.componentInstance.doneSteps()[2]).toBeTrue();
      });
    });

    it('marks step 3 done on click with no network call (manual in Phase 1)', async () => {
      await configureLocked();
      fixture.componentInstance.markSentStep();
      expect(fixture.componentInstance.doneSteps()[3]).toBeTrue();
    });

    it('does not require any step to be done before Log Dispatch is enabled', async () => {
      await configureLocked();
      expect(fixture.componentInstance.doneCount()).toBe(0);
      expect(fixture.componentInstance.canLogDispatch()).toBeTrue();
    });
  });

  describe('logging the dispatch', () => {
    async function configureLocked(): Promise<void> {
      await configure();
      fixture.componentInstance.customerLock = {
        id: 'cust-1',
        businessName: 'Meena Traders',
        subline: 'Meena Shah · +91 98250 41122',
        serviceTypeCode: 'CIF'
      };
      fixture.componentInstance.documentLock = {
        documentId: 'doc-1',
        title: 'AW26 Catalog',
        filename: 'yiwu-jewel-craft-aw26-v3.pdf',
        meta: 'v3 · 8.4 MB · Yiwu Jewel Craft Co.'
      };
      fixture.detectChanges();
      flushMasterData();
      httpMock.expectOne((r) => r.url === '/api/v1/dispatch-log/compose').flush({
        message: 'Hi Meena, sharing the AW26 catalog.',
        deepLinkUrl: 'https://wa.me/919825041122?text=hi'
      });
      fixture.detectChanges();
    }

    it('posts the edited message (no staff field — the server takes it from the token) and emits logged + closed', async () => {
      await configureLocked();
      fixture.componentInstance.onMessageInput('A hand-edited message.');

      let loggedEmitted = false;
      let closedEmitted = false;
      fixture.componentInstance.logged.subscribe(() => (loggedEmitted = true));
      fixture.componentInstance.closed.subscribe(() => (closedEmitted = true));

      fixture.componentInstance.logDispatch();
      const req = httpMock.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/dispatch-log');
      expect(req.request.body).toEqual({
        customerId: 'cust-1',
        catalogDocumentId: 'doc-1',
        message: 'A hand-edited message.'
      });
      req.flush({
        id: 'dlog-1',
        customerId: 'cust-1',
        customerName: 'Meena Traders',
        catalogDocumentId: 'doc-1',
        catalogName: 'AW26 Catalog',
        catalogDocumentFilename: 'yiwu-jewel-craft-aw26-v3.pdf',
        staffUserId: 'user-1',
        staffUserName: 'Priya Sharma',
        message: 'A hand-edited message.',
        sentAtUtc: '2026-07-28T10:00:00Z'
      });

      expect(loggedEmitted).toBeTrue();
      expect(closedEmitted).toBeTrue();
    });

    it('shows a visible error and keeps the dialog open when logging fails', async () => {
      await configureLocked();
      fixture.componentInstance.logDispatch();
      httpMock
        .expectOne((r) => r.method === 'POST' && r.url === '/api/v1/dispatch-log')
        .flush({ title: 'Server error', detail: 'Could not log this dispatch. Please try again.' }, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(fixture.componentInstance.saveError()).toContain('Could not log this dispatch');
      expect(fixture.nativeElement.textContent).toContain('Could not log this dispatch');
    });

    it('disables Log Dispatch once the message is cleared', async () => {
      await configureLocked();
      fixture.componentInstance.onMessageInput('   ');
      expect(fixture.componentInstance.canLogDispatch()).toBeFalse();
    });
  });

  describe('error states', () => {
    it('shows a visible error instead of hanging when the customer list fails to load', async () => {
      await configure();
      fixture.componentInstance.documentLock = {
        documentId: 'doc-1',
        title: 'AW26 Catalog',
        filename: 'yiwu-jewel-craft-aw26-v3.pdf',
        meta: 'v3 · 8.4 MB · Yiwu Jewel Craft Co.'
      };
      fixture.detectChanges();
      flushMasterData();
      httpMock
        .expectOne((r) => r.url === '/api/v1/customers')
        .flush({ title: 'Server error', detail: 'Could not load customers. Please try again.' }, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(fixture.componentInstance.customersError()).toContain('Could not load customers');
      expect(fixture.nativeElement.textContent).toContain('Could not load customers');
    });

    it('shows a retry action instead of hanging when compose fails', async () => {
      await configure();
      fixture.componentInstance.customerLock = {
        id: 'cust-1',
        businessName: 'Meena Traders',
        subline: 'Meena Shah · +91 98250 41122',
        serviceTypeCode: 'CIF'
      };
      fixture.componentInstance.documentLock = {
        documentId: 'doc-1',
        title: 'AW26 Catalog',
        filename: 'yiwu-jewel-craft-aw26-v3.pdf',
        meta: 'v3 · 8.4 MB · Yiwu Jewel Craft Co.'
      };
      fixture.detectChanges();
      flushMasterData();
      httpMock
        .expectOne((r) => r.url === '/api/v1/dispatch-log/compose')
        .flush({ title: 'Not Found', detail: 'Could not prepare this dispatch. Please try again.' }, { status: 404, statusText: 'Not Found' });
      fixture.detectChanges();

      expect(fixture.componentInstance.composeError()).toContain('Could not prepare this dispatch');
      expect(fixture.componentInstance.canLogDispatch()).toBeFalse();

      fixture.componentInstance.retryCompose();
      httpMock
        .expectOne((r) => r.url === '/api/v1/dispatch-log/compose')
        .flush({ message: 'Hi Meena, sharing the AW26 catalog.', deepLinkUrl: 'https://wa.me/919825041122?text=hi' });
      fixture.detectChanges();

      expect(fixture.componentInstance.composeError()).toBeNull();
      expect(fixture.componentInstance.canLogDispatch()).toBeTrue();
    });
  });
});
