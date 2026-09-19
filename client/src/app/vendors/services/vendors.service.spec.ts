import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { VendorsService } from './vendors.service';
import { VendorDetail } from '../models/vendor.models';

const DETAIL: VendorDetail = {
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
  catalogSections: []
};

describe('VendorsService', () => {
  let service: VendorsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(VendorsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('GETs the list with all filter/paging params', () => {
    service.list({ search: 'Yiwu', page: 1, pageSize: 25, categoryId: 'cat-1', region: 'Yiwu', statusId: 'vst-active' }).subscribe();

    const req = httpMock.expectOne(
      (r) =>
        r.url === '/api/v1/vendors' &&
        r.params.get('search') === 'Yiwu' &&
        r.params.get('page') === '1' &&
        r.params.get('pageSize') === '25' &&
        r.params.get('categoryId') === 'cat-1' &&
        r.params.get('region') === 'Yiwu' &&
        r.params.get('statusId') === 'vst-active'
    );
    expect(req.request.method).toBe('GET');
    req.flush({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  });

  it('GETs the full detail shape by id, catalogSections embedded (E5-05)', () => {
    service.getById('ven-1').subscribe();
    const req = httpMock.expectOne('/api/v1/vendors/ven-1');
    expect(req.request.method).toBe('GET');
    req.flush(DETAIL);
  });

  it('POSTs a create request', () => {
    service.create({ name: 'New Vendor', statusId: 'vst-active' }).subscribe();
    const req = httpMock.expectOne('/api/v1/vendors');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.name).toBe('New Vendor');
    req.flush(DETAIL, { status: 201, statusText: 'Created' });
  });

  it('PUTs an update request', () => {
    service.update('ven-1', { name: 'Yiwu Jewel Craft Co.', statusId: 'vst-active' }).subscribe();
    const req = httpMock.expectOne('/api/v1/vendors/ven-1');
    expect(req.request.method).toBe('PUT');
    req.flush(DETAIL);
  });

  it('GETs the compliance-document list for a vendor and unwraps the { items } envelope', () => {
    let result: unknown;
    service.listDocuments('ven-1').subscribe((docs) => (result = docs));
    const req = httpMock.expectOne('/api/v1/vendors/ven-1/documents');
    expect(req.request.method).toBe('GET');
    req.flush({ items: [] });
    expect(result).toEqual([]);
  });

  /**
   * D-64: the request body's field name is asserted against the literal
   * `'docTypeId'`, never against a constant imported from the production
   * code — an identity assertion that terminated in the same constant the
   * code under test uses would pass even if `VendorsService.uploadDocument`
   * silently drifted to the shipment upload's `documentTypeId` field name
   * (the exact mistake this test exists to catch, since `VendorsController`
   * binds `[FromForm] Guid docTypeId`, live-confirmed against the controller
   * source — NOT `documentTypeId`, which is `ShipmentsController`'s field).
   */
  it('uploads a document as multipart form data with the docTypeId field (NOT documentTypeId)', () => {
    const file = new File(['content'], 'business-licence.pdf', { type: 'application/pdf' });
    service.uploadDocument('ven-1', file, 'dt-1').subscribe();

    const req = httpMock.expectOne('/api/v1/vendors/ven-1/documents');
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBeTrue();
    const body = req.request.body as FormData;
    expect(body.get('docTypeId')).toBe('dt-1');
    expect(body.get('documentTypeId')).toBeNull();
    req.flush({
      id: 'vdoc-1',
      vendorId: 'ven-1',
      originalFilename: 'business-licence.pdf',
      sizeBytes: 2048,
      docType: { id: 'dt-1', code: 'BUSINESS_LICENCE', label: 'Business Licence' },
      uploadedByUserId: 'user-1',
      uploadedByName: 'Priya Sharma',
      uploadedAt: '2026-07-29T10:00:00Z'
    });
  });

  it('downloads a document from the NOT-nested /vendor-documents path', () => {
    service.downloadDocument('vdoc-1').subscribe();
    const req = httpMock.expectOne('/api/v1/vendor-documents/vdoc-1/download');
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob());
  });

  it('deletes a document from the NOT-nested /vendor-documents path', () => {
    service.deleteDocument('vdoc-1').subscribe();
    const req = httpMock.expectOne('/api/v1/vendor-documents/vdoc-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
