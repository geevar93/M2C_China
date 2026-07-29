import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { VendorFormDialogComponent } from './vendor-form-dialog.component';
import { MasterDataResponse } from '../../core/models/master-data.models';
import { VendorDetail } from '../models/vendor.models';

const MASTER_DATA: MasterDataResponse = {
  categories: [
    { id: 'cat-jewellery', name: 'Jewellery', sortOrder: 1, isActive: true },
    { id: 'cat-handbags', name: 'Handbags', sortOrder: 2, isActive: true }
  ],
  serviceTypes: [],
  leadStatuses: [],
  shipmentStatuses: [],
  invoiceStatuses: [],
  vendorStatuses: [
    { id: 'vst-active', code: 'ACTIVE', label: 'Active', sortOrder: 1, isActive: true },
    { id: 'vst-hold', code: 'ON-HOLD', label: 'On Hold', sortOrder: 2, isActive: true }
  ]
};

const existingVendor: VendorDetail = {
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
  email: null,
  paymentTerms: null,
  notes: null,
  createdAt: '2026-01-10T00:00:00Z',
  catalogSections: []
};

describe('VendorFormDialogComponent', () => {
  let fixture: ComponentFixture<VendorFormDialogComponent>;
  let httpMock: HttpTestingController;

  async function configure(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [VendorFormDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    fixture = TestBed.createComponent(VendorFormDialogComponent);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  function flushMasterData(): void {
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(MASTER_DATA);
  }

  it('creates a vendor via POST /vendors when no vendor input is set', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    fixture.detectChanges();

    const c = fixture.componentInstance;
    c.name.set('Guangzhou Leather Works');
    c.contactPerson.set('Chen Hui');
    c.phone.set('+86 138 2201 7783');
    c.region.set('Guangzhou, Guangdong');
    c.statusId.set('vst-active');
    c.toggleCategory('cat-handbags');
    c.save();

    const req = httpMock.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/vendors');
    expect(req.request.body.categoryIds).toEqual(['cat-handbags']);
    expect(req.request.body.statusId).toBe('vst-active');
    req.flush({ ...existingVendor, id: 'ven-2', name: 'Guangzhou Leather Works' });
  });

  it('prefills every field from the vendor input and updates via PUT /vendors/{id}', async () => {
    await configure();
    fixture.componentInstance.vendor = existingVendor;
    fixture.detectChanges();
    flushMasterData();
    fixture.detectChanges();

    const c = fixture.componentInstance;
    expect(c.name()).toBe('Yiwu Jewel Craft Co.');
    expect(c.selectedCategoryIds()).toEqual(['cat-jewellery']);
    expect(c.statusId()).toBe('vst-active');
    expect(c.moq()).toBe('300 sets');

    c.moq.set('350 sets');
    c.save();

    const req = httpMock.expectOne((r) => r.method === 'PUT' && r.url === '/api/v1/vendors/ven-1');
    expect(req.request.body.moq).toBe('350 sets');
    req.flush({ ...existingVendor, moq: '350 sets' });
  });

  it('blocks save with a validation message when required fields are missing', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    fixture.detectChanges();

    fixture.componentInstance.save();

    expect(fixture.componentInstance.error()).toContain('required');
    httpMock.expectNone('/api/v1/vendors');
  });

  it('rejects a reliability rating outside 0–5', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    fixture.detectChanges();

    const c = fixture.componentInstance;
    c.name.set('X');
    c.contactPerson.set('Y');
    c.phone.set('Z');
    c.region.set('R');
    c.statusId.set('vst-active');
    c.reliabilityRating.set('7');
    c.save();

    expect(fixture.componentInstance.error()).toContain('between 0 and 5');
    httpMock.expectNone('/api/v1/vendors');
  });

  it('surfaces the ProblemDetails message when the save fails', async () => {
    await configure();
    fixture.detectChanges();
    flushMasterData();
    fixture.detectChanges();

    const c = fixture.componentInstance;
    c.name.set('X');
    c.contactPerson.set('Y');
    c.phone.set('Z');
    c.region.set('R');
    c.statusId.set('vst-active');
    c.save();

    httpMock
      .expectOne((r) => r.url === '/api/v1/vendors')
      .flush({ title: 'Conflict', detail: 'A vendor with this name already exists' }, { status: 409, statusText: 'Conflict' });

    expect(fixture.componentInstance.saving()).toBeFalse();
    expect(fixture.componentInstance.error()).toBe('A vendor with this name already exists');
  });
});
