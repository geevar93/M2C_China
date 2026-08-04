import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { CustomerIntakeComponent } from './intake.component';
import { MasterDataResponse } from '../core/models/master-data.models';
import { CustomerDetail, CustomerListItem } from './models/customer.models';

const MASTER_DATA: MasterDataResponse = {
  categories: [
    { id: 'cat-jewellery', name: 'Jewellery', sortOrder: 1, isActive: true },
    { id: 'cat-retired', name: 'Legacy Category', sortOrder: 2, isActive: false }
  ],
  serviceTypes: [
    { id: 'svc-cif', code: 'CIF', label: 'CIF', sortOrder: 1, isActive: true },
    { id: 'svc-freight', code: 'FREIGHT_ONLY', label: 'Freight-only', sortOrder: 2, isActive: true }
  ],
  leadStatuses: [
    { id: 'lead-new', code: 'NEW', label: 'New', sortOrder: 1, isActive: true },
    { id: 'lead-qualified', code: 'QUALIFIED', label: 'Qualified', sortOrder: 2, isActive: true }
  ],
  shipmentStatuses: [],
  invoiceStatuses: [],
  vendorStatuses: [],
  documentTypes: []
};

const existingCustomer: CustomerListItem = {
  id: 'cust-existing',
  name: 'Existing Person',
  businessName: 'Existing Traders',
  phone: '+91 9825041122',
  city: 'Surat',
  region: null,
  sourceChannel: 'WhatsApp',
  serviceTypeId: 'svc-cif',
  statusId: 'lead-new',
  categoryIds: [],
  ownerUserId: 'user-1',
  ownerName: 'Priya Sharma',
  tags: [],
  createdAt: '2026-02-11T00:00:00Z'
};

const savedDetail: CustomerDetail = {
  ...existingCustomer,
  id: 'cust-new',
  email: null,
  gstin: null,
  notes: null,
  externalMarketplace: null,
  externalOrderRef: null,
  externalSupplierName: null,
  externalOrderValue: null,
  externalOrderCurrency: null,
  externalOrderDate: null
};

describe('CustomerIntakeComponent', () => {
  let fixture: ComponentFixture<CustomerIntakeComponent>;
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CustomerIntakeComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(CustomerIntakeComponent);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => httpMock.verify());

  function flushMasterData(): void {
    httpMock.expectOne((r) => r.url === '/api/v1/master-data').flush(MASTER_DATA);
  }

  function fillRequiredFields(): void {
    const c = fixture.componentInstance;
    c.form.controls.name.setValue('Meena Shah');
    c.form.controls.businessName.setValue('Meena Traders');
    c.form.controls.phone.setValue('9825041122');
    c.form.controls.sourceChannel.setValue('WhatsApp');
  }

  it('populates service-type, status and category options from MasterDataService, not a hard-coded list', () => {
    fixture.detectChanges();
    flushMasterData();
    fixture.detectChanges();

    const html: string = fixture.nativeElement.innerHTML;
    expect(html).toContain('Jewellery');
    expect(html).not.toContain('Legacy Category'); // retired — must not appear as a selectable chip
    expect(html).toContain('CIF');
    expect(html).toContain('FREIGHT-ONLY'); // StatusStyleService's pill label for the 'Freight-only' code
    expect(html).toContain('Transport only'); // service-type card copy, keyed off the dynamically-listed code
    expect(html).toContain('Qualified');
  });

  it('defaults the service type to CIF and hides the freight-only external fields', () => {
    fixture.detectChanges();
    flushMasterData();
    fixture.detectChanges();

    expect(fixture.componentInstance.showExtRef).toBeFalse();
  });

  it('shows the external-purchase fields only once Freight-only is picked', () => {
    fixture.detectChanges();
    flushMasterData();
    fixture.detectChanges();

    fixture.componentInstance.pickServiceType('svc-freight');
    fixture.detectChanges();

    expect(fixture.componentInstance.showExtRef).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('External Marketplace');
  });

  it('shows a duplicate-confirmation dialog on 409 and retries with confirmDuplicate: true on confirm', () => {
    fixture.detectChanges();
    flushMasterData();
    fixture.detectChanges();
    fillRequiredFields();

    fixture.componentInstance.save();

    const firstReq = httpMock.expectOne((r) => r.url === '/api/v1/customers');
    expect(firstReq.request.body.confirmDuplicate).toBeUndefined();
    firstReq.flush(
      { title: 'Conflict', detail: 'Phone already in use', existingCustomer: existingCustomer },
      { status: 409, statusText: 'Conflict' }
    );
    fixture.detectChanges();

    expect(fixture.componentInstance.duplicate()).toBeTruthy();
    expect(fixture.componentInstance.duplicate()?.businessName).toBe('Existing Traders');
    expect(fixture.nativeElement.textContent).toContain('Possible duplicate');

    fixture.componentInstance.confirmDuplicateSave();
    const retryReq = httpMock.expectOne((r) => r.url === '/api/v1/customers');
    expect(retryReq.request.body.confirmDuplicate).toBeTrue();

    const navigateSpy = spyOn(router, 'navigate');
    retryReq.flush(savedDetail, { status: 201, statusText: 'Created' });

    expect(fixture.componentInstance.duplicate()).toBeNull();
    expect(navigateSpy).toHaveBeenCalledWith(['/customers', 'cust-new']);
  });

  it('does not send the freight-only external fields when the service type is CIF', () => {
    fixture.detectChanges();
    flushMasterData();
    fixture.detectChanges();
    fillRequiredFields();

    fixture.componentInstance.save();

    const req = httpMock.expectOne((r) => r.url === '/api/v1/customers');
    expect(req.request.body.externalMarketplace).toBeNull();
    expect(req.request.body.externalOrderRef).toBeNull();
    req.flush(savedDetail, { status: 201, statusText: 'Created' });
  });

  describe('GSTIN (N-37)', () => {
    it('sends null and stays valid when GSTIN is left empty', () => {
      fixture.detectChanges();
      flushMasterData();
      fixture.detectChanges();
      fillRequiredFields();

      fixture.componentInstance.save();

      expect(fixture.componentInstance.form.invalid).toBeFalse();
      const req = httpMock.expectOne((r) => r.url === '/api/v1/customers');
      expect(req.request.body.gstin).toBeNull();
      req.flush(savedDetail, { status: 201, statusText: 'Created' });
    });

    it('uppercases a valid 15-character value and sends it as-typed-but-uppercased', () => {
      fixture.detectChanges();
      flushMasterData();
      fixture.detectChanges();
      fillRequiredFields();
      fixture.componentInstance.form.controls.gstin.setValue('24aaaaa0000a1z5');

      fixture.componentInstance.save();

      const req = httpMock.expectOne((r) => r.url === '/api/v1/customers');
      expect(req.request.body.gstin).toBe('24AAAAA0000A1Z5');
      req.flush(savedDetail, { status: 201, statusText: 'Created' });
    });

    it('uppercases the field value itself on blur', () => {
      fixture.detectChanges();
      flushMasterData();
      fixture.detectChanges();

      fixture.componentInstance.form.controls.gstin.setValue('24aaaaa0000a1z5');
      fixture.componentInstance.onGstinBlur();

      expect(fixture.componentInstance.form.controls.gstin.value).toBe('24AAAAA0000A1Z5');
    });

    it('rejects a value that is not 15 alphanumeric characters and blocks submit', () => {
      fixture.detectChanges();
      flushMasterData();
      fixture.detectChanges();
      fillRequiredFields();
      fixture.componentInstance.form.controls.gstin.setValue('TOO-SHORT');

      fixture.componentInstance.save();
      fixture.detectChanges();

      expect(fixture.componentInstance.form.controls.gstin.invalid).toBeTrue();
      expect(fixture.nativeElement.textContent).toContain('GSTIN must be 15 alphanumeric characters.');
      httpMock.expectNone((r) => r.url === '/api/v1/customers');
    });

    it('surfaces a server-side 400 field error for gstin on the field itself', () => {
      fixture.detectChanges();
      flushMasterData();
      fixture.detectChanges();
      fillRequiredFields();
      fixture.componentInstance.form.controls.gstin.setValue('24AAAAA0000A1Z5');

      fixture.componentInstance.save();

      const req = httpMock.expectOne((r) => r.url === '/api/v1/customers');
      req.flush(
        { title: 'Bad Request', errors: { gstin: ['GSTIN must be 15 alphanumeric characters.'] } },
        { status: 400, statusText: 'Bad Request' }
      );
      fixture.detectChanges();

      expect(fixture.componentInstance.gstinServerError()).toBe('GSTIN must be 15 alphanumeric characters.');
      expect(fixture.nativeElement.textContent).toContain('GSTIN must be 15 alphanumeric characters.');
    });
  });

  describe('tag editor (E4-11)', () => {
    it('adds a trimmed tag and clears the input', () => {
      fixture.detectChanges();
      flushMasterData();
      fixture.detectChanges();

      fixture.componentInstance.tagInput.set('  Repeat Buyer  ');
      fixture.componentInstance.addTag();

      expect(fixture.componentInstance.tags()).toEqual(['Repeat Buyer']);
      expect(fixture.componentInstance.tagInput()).toBe('');
    });

    it('ignores an empty or whitespace-only tag', () => {
      fixture.detectChanges();
      flushMasterData();
      fixture.detectChanges();

      fixture.componentInstance.tagInput.set('   ');
      fixture.componentInstance.addTag();

      expect(fixture.componentInstance.tags()).toEqual([]);
    });

    it('rejects a case-insensitive duplicate of a tag already added', () => {
      fixture.detectChanges();
      flushMasterData();
      fixture.detectChanges();

      fixture.componentInstance.tagInput.set('VIP');
      fixture.componentInstance.addTag();
      fixture.componentInstance.tagInput.set('vip');
      fixture.componentInstance.addTag();

      expect(fixture.componentInstance.tags()).toEqual(['VIP']);
    });

    it('removes a tag', () => {
      fixture.detectChanges();
      flushMasterData();
      fixture.detectChanges();

      fixture.componentInstance.tagInput.set('VIP');
      fixture.componentInstance.addTag();
      fixture.componentInstance.tagInput.set('Repeat Buyer');
      fixture.componentInstance.addTag();
      fixture.componentInstance.removeTag('VIP');

      expect(fixture.componentInstance.tags()).toEqual(['Repeat Buyer']);
    });

    it('sends the added tags on save', () => {
      fixture.detectChanges();
      flushMasterData();
      fixture.detectChanges();
      fillRequiredFields();
      fixture.componentInstance.tagInput.set('VIP');
      fixture.componentInstance.addTag();

      fixture.componentInstance.save();

      const req = httpMock.expectOne((r) => r.url === '/api/v1/customers');
      expect(req.request.body.tags).toEqual(['VIP']);
      req.flush(savedDetail, { status: 201, statusText: 'Created' });
    });
  });
});
