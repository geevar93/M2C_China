import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { VendorDetailComponent } from './vendor-detail.component';
import { AuthService } from '../../core/services/auth.service';
import { VendorDetail, VendorDocument } from '../models/vendor.models';

const detail: VendorDetail = {
  id: 'ven-1',
  name: 'Yiwu Jewel Craft Co.',
  contactPerson: 'Li Wen',
  phone: '+86 137 5829 4410',
  region: 'Yiwu, Zhejiang',
  categories: [{ id: 'cat-jewellery', name: 'Jewellery' }],
  status: { id: 'vst-active', code: 'ACTIVE', label: 'Active' },
  moq: '300 sets',
  leadTime: '18 days',
  reliabilityRating: 4.6,
  catalogCount: 1,
  email: 'li.wen@example.cn',
  paymentTerms: '30% deposit, balance before shipment',
  notes: null,
  createdAt: '2026-01-10T00:00:00Z',
  catalogSections: [
    {
      id: 'sec-1',
      vendorId: 'ven-1',
      vendorName: 'Yiwu Jewel Craft Co.',
      title: 'Yiwu Jewel Craft — Jewellery — AW26 Catalog',
      category: { id: 'cat-jewellery', name: 'Jewellery' },
      tags: ['NEW ARRIVALS'],
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
        },
        {
          id: 'doc-0',
          catalogSectionId: 'sec-1',
          originalFilename: 'yiwu-jewel-craft-aw26-v2.pdf',
          sizeBytes: 8500000,
          versionLabel: 'v2',
          isLatest: false,
          uploadedByUserId: 'user-1',
          uploadedByName: 'Priya S.',
          uploadedAt: '2026-06-02T00:00:00Z'
        }
      ]
    }
  ]
};

describe('VendorDetailComponent', () => {
  let fixture: ComponentFixture<VendorDetailComponent>;
  let httpMock: HttpTestingController;

  async function configure(id = 'ven-1', permissions: string[] = ['Vendors.Edit', 'Catalogs.Edit']): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [VendorDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of(convertToParamMap({ id })),
            snapshot: { paramMap: convertToParamMap({ id }) }
          }
        },
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p), currentUser$: of(null) } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(VendorDetailComponent);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  function flushVendor(payload: VendorDetail = detail): void {
    httpMock.expectOne((r) => r.url === '/api/v1/vendors/ven-1').flush(payload);
  }

  /** The vendor-documents list is a separate `GET /vendors/{id}/documents` call, returned in an `{ items }` envelope (VendorDocumentListResultDto). */
  function flushDocuments(docs: VendorDocument[] = []): void {
    httpMock.expectOne((r) => r.url === '/api/v1/vendors/ven-1/documents').flush({ items: docs });
  }

  it('renders the profile subline, stat tiles and status chip colour from a single GET /vendors/{id} (E5-05, no second call)', async () => {
    await configure();
    fixture.detectChanges();
    flushVendor();
    flushDocuments();
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Li Wen · Yiwu, Zhejiang · +86 137 5829 4410 · Jewellery');
    expect(text).toContain('300 sets');
    expect(text).toContain('18 days');
    expect(text).toContain('4.6');
    expect(fixture.componentInstance.statusChip().fg).toBe('#2e7d32'); // ACTIVE
  });

  it('renders each catalog section\'s documents, marking the latest version distinctly', async () => {
    await configure();
    fixture.detectChanges();
    flushVendor();
    flushDocuments();
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('yiwu-jewel-craft-aw26-v3.pdf');
    expect(text).toContain('v3 · LATEST');
    expect(text).toContain('v2');
    expect(text).not.toContain('v2 · LATEST');
    expect(text).toContain('NEW ARRIVALS');
  });

  it('shows a visible error instead of hanging when the vendor fails to load', async () => {
    await configure();
    fixture.detectChanges();
    httpMock
      .expectOne((r) => r.url === '/api/v1/vendors/ven-1')
      .flush({ title: 'Not Found', detail: 'Vendor not found' }, { status: 404, statusText: 'Not Found' });
    flushDocuments();
    fixture.detectChanges();

    expect(fixture.componentInstance.loading()).toBeFalse();
    expect(fixture.componentInstance.error()).toBe('Vendor not found');
    expect(fixture.nativeElement.textContent).toContain('Vendor not found');
  });

  it('hides Edit Vendor / Upload Catalog PDF without the matching permissions', async () => {
    await configure('ven-1', []);
    fixture.detectChanges();
    flushVendor();
    flushDocuments();
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).not.toContain('Edit Vendor');
    expect(text).not.toContain('+ Upload Catalog PDF');
  });

  it('disables "Send via WhatsApp" without Dispatch.Send (E9-05)', async () => {
    await configure('ven-1', ['Vendors.Edit', 'Catalogs.Edit']);
    fixture.detectChanges();
    flushVendor();
    flushDocuments();
    fixture.detectChanges();

    const btn: HTMLButtonElement = fixture.nativeElement.querySelector('[aria-label="Send via WhatsApp"]');
    expect(btn.disabled).toBeTrue();
  });

  it('opens the dispatch dialog document-locked to that row\'s document with Dispatch.Send', async () => {
    await configure('ven-1', ['Vendors.Edit', 'Catalogs.Edit', 'Dispatch.Send']);
    fixture.detectChanges();
    flushVendor();
    flushDocuments();
    fixture.detectChanges();

    const btns: HTMLButtonElement[] = fixture.nativeElement.querySelectorAll('[aria-label="Send via WhatsApp"]');
    expect(btns[0].disabled).toBeFalse();

    btns[0].click();
    fixture.detectChanges();

    expect(fixture.componentInstance.dispatchOpen()).toBeTrue();
    expect(fixture.componentInstance.dispatchDocumentLock()).toEqual({
      documentId: 'doc-1',
      title: 'Yiwu Jewel Craft — Jewellery — AW26 Catalog',
      filename: 'yiwu-jewel-craft-aw26-v3.pdf',
      meta: 'v3 · 8.4 MB · Yiwu Jewel Craft Co.'
    });

    // The dialog's own child-load requests (master data + customers, since no
    // customerLock was supplied) — drained so httpMock.verify() doesn't fail
    // the outer spec. Vendor-detail itself never touches MasterDataService,
    // so this is the dialog's first-ever load of it in this test.
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush({
      categories: [],
      serviceTypes: [],
      leadStatuses: [],
      shipmentStatuses: [],
      invoiceStatuses: [],
      vendorStatuses: []
    });
    httpMock.expectOne((r) => r.url === '/api/v1/customers').flush({ items: [], page: 1, pageSize: 200, totalCount: 0 });
  });

  it('expands a "Sent to" history panel per document row (E9-07), newest first', async () => {
    await configure();
    fixture.detectChanges();
    flushVendor();
    flushDocuments();
    fixture.detectChanges();

    fixture.componentInstance.toggleHistory('doc-1');
    fixture.detectChanges();

    const req = httpMock.expectOne((r) => r.url === '/api/v1/catalog-documents/doc-1/dispatches');
    req.flush([
      { dispatchId: 'd1', customerId: 'cust-1', customerName: 'Meena Traders', staffUserId: 'user-1', staffUserName: 'Priya Sharma', sentAtUtc: '2026-07-18T14:22:00Z' }
    ]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Meena Traders');
    expect(fixture.nativeElement.textContent).toContain('Priya Sharma');
  });

  it('shows an empty state instead of hanging when a document has no dispatch history', async () => {
    await configure();
    fixture.detectChanges();
    flushVendor();
    flushDocuments();
    fixture.detectChanges();

    fixture.componentInstance.toggleHistory('doc-1');
    httpMock.expectOne((r) => r.url === '/api/v1/catalog-documents/doc-1/dispatches').flush([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Not sent to any customer yet');
  });

  it('shows a visible error with retry instead of hanging when the history fetch fails', async () => {
    await configure();
    fixture.detectChanges();
    flushVendor();
    flushDocuments();
    fixture.detectChanges();

    fixture.componentInstance.toggleHistory('doc-1');
    httpMock
      .expectOne((r) => r.url === '/api/v1/catalog-documents/doc-1/dispatches')
      .flush({ title: 'Server error', detail: 'History lookup failed' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('History lookup failed');

    fixture.componentInstance.retryHistory('doc-1');
    httpMock.expectOne((r) => r.url === '/api/v1/catalog-documents/doc-1/dispatches').flush([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Not sent to any customer yet');
  });

  it('previews a document through the authenticated download endpoint, never a static href', async () => {
    await configure();
    fixture.detectChanges();
    flushVendor();
    flushDocuments();
    fixture.detectChanges();

    const openSpy = spyOn(window, 'open').and.stub();
    spyOn(URL, 'createObjectURL').and.returnValue('blob:mock-url');

    fixture.componentInstance.previewDocument('doc-1');

    const req = httpMock.expectOne((r) => r.url === '/api/v1/catalog-documents/doc-1/download');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['%PDF-1.4'], { type: 'application/pdf' }));

    expect(openSpy).toHaveBeenCalledWith('blob:mock-url', '_blank');
  });

  it('opens the vendor-locked upload dialog and reloads the vendor after a successful upload', async () => {
    await configure();
    fixture.detectChanges();
    flushVendor();
    flushDocuments();
    fixture.detectChanges();

    fixture.componentInstance.openUpload();
    fixture.detectChanges();
    expect(fixture.componentInstance.uploadOpen()).toBeTrue();

    // The (vendor-locked) upload dialog child component initializes itself —
    // it shares the app-wide MasterDataService cache and fetches this
    // vendor's existing catalog sections for "add to existing section" mode.
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush({
      categories: [],
      serviceTypes: [],
      leadStatuses: [],
      shipmentStatuses: [],
      invoiceStatuses: [],
      vendorStatuses: []
    });
    httpMock
      .expectOne((r) => r.url === '/api/v1/catalog-sections')
      .flush({ items: detail.catalogSections, page: 1, pageSize: 100, totalCount: 1 });
    fixture.detectChanges();

    fixture.componentInstance.onUploadSaved(detail.catalogSections[0]);
    flushVendor();
    flushDocuments();
    fixture.detectChanges();

    expect(fixture.componentInstance.uploadOpen()).toBeFalse();
  });

  it('regression: the edit dialog renders its saved values and button labels with the real { items } documents payload', async () => {
    await configure();
    fixture.detectChanges();
    flushVendor();
    flushDocuments();
    fixture.detectChanges();

    fixture.componentInstance.openEdit();
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush({
      categories: [], serviceTypes: [], leadStatuses: [], shipmentStatuses: [], invoiceStatuses: [], vendorStatuses: []
    });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect((el.querySelector('#vf-name') as HTMLInputElement).value).toBe('Yiwu Jewel Craft Co.');
    expect(el.querySelector('.dialog-title')?.textContent).toContain('Edit Vendor');
    expect(el.textContent).toContain('Save Vendor');
  });

  it('opens the edit-vendor dialog prefilled and applies the saved result without a refetch', async () => {
    await configure();
    fixture.detectChanges();
    flushVendor();
    flushDocuments();
    fixture.detectChanges();

    fixture.componentInstance.openEdit();
    fixture.detectChanges();
    expect(fixture.componentInstance.editOpen()).toBeTrue();

    // The edit-vendor dialog child component shares the app-wide
    // MasterDataService cache for its category/vendor-status pickers.
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush({
      categories: [],
      serviceTypes: [],
      leadStatuses: [],
      shipmentStatuses: [],
      invoiceStatuses: [],
      vendorStatuses: []
    });
    fixture.detectChanges();

    const updated: VendorDetail = { ...detail, moq: '350 sets' };
    fixture.componentInstance.onVendorSaved(updated);
    fixture.detectChanges();

    expect(fixture.componentInstance.editOpen()).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('350 sets');
  });

  // ---- E5-10: Compliance Documents ---------------------------------------

  const complianceDoc: VendorDocument = {
    id: 'vdoc-1',
    vendorId: 'ven-1',
    originalFilename: 'business-licence.pdf',
    sizeBytes: 204800,
    docType: { id: 'dt-1', code: 'BUSINESS_LICENCE', label: 'Business Licence' },
    uploadedByUserId: 'user-1',
    uploadedByName: 'Priya Sharma',
    uploadedAt: '2026-07-20T00:00:00Z'
  };

  it('lists compliance documents from the separate GET /vendors/{id}/documents call', async () => {
    await configure();
    fixture.detectChanges();
    flushVendor();
    flushDocuments([complianceDoc]);
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Compliance Documents');
    expect(text).toContain('business-licence.pdf');
    expect(text).toContain('Business Licence');
    expect(text).toContain('200.0 KB');
  });

  it('shows an empty state instead of hanging when a vendor has no compliance documents', async () => {
    await configure();
    fixture.detectChanges();
    flushVendor();
    flushDocuments([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No compliance documents attached yet.');
  });

  it('shows a visible error with retry instead of hanging when the compliance-documents fetch fails', async () => {
    await configure();
    fixture.detectChanges();
    flushVendor();
    httpMock
      .expectOne((r) => r.url === '/api/v1/vendors/ven-1/documents')
      .flush({ title: 'Server error', detail: 'Could not load documents' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Could not load documents');

    fixture.componentInstance.retryDocuments();
    flushDocuments([complianceDoc]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('business-licence.pdf');
  });

  it('hides "+ Attach Document" without Vendors.Edit', async () => {
    await configure('ven-1', ['Catalogs.Edit']);
    fixture.detectChanges();
    flushVendor();
    flushDocuments();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('+ Attach Document');
  });

  it('opens the vendor-locked attach-document dialog and reloads the document list after a successful upload', async () => {
    await configure();
    fixture.detectChanges();
    flushVendor();
    flushDocuments();
    fixture.detectChanges();

    fixture.componentInstance.openDocumentUpload();
    fixture.detectChanges();
    expect(fixture.componentInstance.documentUploadOpen()).toBeTrue();

    // The dialog's own child-load — shares the app-wide MasterDataService
    // cache. Unlike the other child dialogs in this spec, this one calls
    // `documentTypeOptions('Vendor')`, so the flushed payload must include
    // `documentTypes` (N-20(a)) or `MasterDataService`'s `activeSorted` throws.
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush({
      categories: [],
      serviceTypes: [],
      leadStatuses: [],
      shipmentStatuses: [],
      invoiceStatuses: [],
      vendorStatuses: [],
      documentTypes: [{ id: 'dt-1', code: 'BUSINESS_LICENCE', label: 'Business Licence', sortOrder: 1, isActive: true, isSystemDefault: false, scope: 'Vendor' }]
    });
    fixture.detectChanges();

    fixture.componentInstance.onDocumentUploaded(complianceDoc);
    flushDocuments([complianceDoc]);
    fixture.detectChanges();

    expect(fixture.componentInstance.documentUploadOpen()).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('business-licence.pdf');
  });

  it('downloads a compliance document through the authenticated /vendor-documents/{id}/download endpoint, never a static href', async () => {
    await configure();
    fixture.detectChanges();
    flushVendor();
    flushDocuments([complianceDoc]);
    fixture.detectChanges();

    spyOn(URL, 'createObjectURL').and.returnValue('blob:mock-url');
    spyOn(URL, 'revokeObjectURL');
    const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');

    fixture.componentInstance.openComplianceDocument({ id: 'vdoc-1', fileName: 'business-licence.pdf', meta: '' });

    const req = httpMock.expectOne((r) => r.url === '/api/v1/vendor-documents/vdoc-1/download');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['%PDF-1.4'], { type: 'application/pdf' }));

    expect(clickSpy).toHaveBeenCalled();
  });
});
