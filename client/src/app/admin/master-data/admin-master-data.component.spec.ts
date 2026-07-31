import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminMasterDataComponent } from './admin-master-data.component';
import { MasterDataAggregate } from '../models/admin-master-data.models';

const AGGREGATE: MasterDataAggregate = {
  categories: [
    { id: 'cat-1', name: 'Jewellery', sortOrder: 1, isActive: true, isSystemDefault: true },
    { id: 'cat-2', name: 'Handbags', sortOrder: 2, isActive: true, isSystemDefault: false },
    { id: 'cat-3', name: 'Retired Cat', sortOrder: 3, isActive: false, isSystemDefault: false }
  ],
  serviceTypes: [
    { id: 'svc-1', code: 'CIF', label: 'CIF', sortOrder: 1, isActive: true, isSystemDefault: true },
    { id: 'svc-2', code: 'FREIGHT_ONLY', label: 'Freight-only', sortOrder: 2, isActive: true, isSystemDefault: true }
  ],
  leadStatuses: [],
  shipmentStatuses: [],
  invoiceStatuses: [],
  vendorStatuses: [],
  documentTypes: [
    { id: 'doc-1', code: 'BUSINESS_LICENCE', label: 'Business Licence', sortOrder: 1, isActive: true, isSystemDefault: true, scope: 'Vendor' },
    { id: 'doc-2', code: 'PACKING_LIST', label: 'Packing List', sortOrder: 5, isActive: true, isSystemDefault: true, scope: 'Shipment' }
  ]
};

describe('AdminMasterDataComponent', () => {
  let fixture: ComponentFixture<AdminMasterDataComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminMasterDataComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    fixture = TestBed.createComponent(AdminMasterDataComponent);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function flushAggregate(): void {
    httpMock.expectOne((r) => r.url === '/api/v1/master-data' && r.params.get('includeRetired') === 'true').flush(AGGREGATE);
  }

  it('renders the categories tab by default, hiding retired rows until "Show retired" is on', () => {
    fixture.detectChanges();
    flushAggregate();
    fixture.detectChanges();

    let text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Jewellery');
    expect(text).toContain('Handbags');
    expect(text).not.toContain('Retired Cat');

    fixture.componentInstance.toggleIncludeRetired();
    fixture.detectChanges();

    text = fixture.nativeElement.textContent;
    expect(text).toContain('Retired Cat');
    expect(text).toContain('Inactive');
  });

  describe('categories-vs-lookup form asymmetry', () => {
    it('shows a single Name field for the categories tab', () => {
      fixture.detectChanges();
      flushAggregate();
      fixture.detectChanges();

      fixture.componentInstance.openCreateForm();
      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      expect(el.querySelector('#md-name')).toBeTruthy();
      expect(el.querySelector('#md-code')).toBeFalsy();
      expect(el.querySelector('#md-label')).toBeFalsy();
    });

    it('shows separate Code/Label fields for a non-category tab', () => {
      fixture.detectChanges();
      flushAggregate();
      fixture.detectChanges();

      fixture.componentInstance.selectTab('serviceTypes');
      fixture.componentInstance.openCreateForm();
      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      expect(el.querySelector('#md-name')).toBeFalsy();
      expect(el.querySelector('#md-code')).toBeTruthy();
      expect(el.querySelector('#md-label')).toBeTruthy();
    });

    it('POSTs only { name } when creating a category', () => {
      fixture.detectChanges();
      flushAggregate();

      const comp = fixture.componentInstance;
      comp.openCreateForm();
      comp.formName.set('Footwear');
      comp.submitForm();

      const req = httpMock.expectOne('/api/v1/master-data/categories');
      expect(req.request.body).toEqual({ name: 'Footwear' });
      req.flush({ id: 'cat-new', name: 'Footwear', sortOrder: 4, isActive: true, isSystemDefault: false }, { status: 201, statusText: 'Created' });
      flushAggregate();
    });

    it('POSTs { code, label } when creating a lookup row', () => {
      fixture.detectChanges();
      flushAggregate();

      const comp = fixture.componentInstance;
      comp.selectTab('serviceTypes');
      comp.openCreateForm();
      comp.formCode.set('BOND');
      comp.formLabel.set('Bonded warehouse');
      comp.submitForm();

      const req = httpMock.expectOne('/api/v1/master-data/service-types');
      expect(req.request.body).toEqual({ code: 'BOND', label: 'Bonded warehouse' });
      req.flush(
        { id: 'svc-new', code: 'BOND', label: 'Bonded warehouse', sortOrder: 3, isActive: true, isSystemDefault: false },
        { status: 201, statusText: 'Created' }
      );
      flushAggregate();
    });
  });

  // N-20b / D-34. Before the selector existed, every type created here silently
  // defaulted to Vendor scope and could therefore never back a shipment upload —
  // the FSD §3.3 configurability violation DR-6 exists to catch.
  describe('document-type scope selector', () => {
    it('offers the scope field only on the documentTypes tab', () => {
      fixture.detectChanges();
      flushAggregate();
      fixture.detectChanges();

      const comp = fixture.componentInstance;
      const el: HTMLElement = fixture.nativeElement;

      comp.selectTab('serviceTypes');
      comp.openCreateForm();
      fixture.detectChanges();
      expect(el.querySelector('#md-scope')).toBeFalsy();

      comp.cancelForm();
      comp.selectTab('documentTypes');
      comp.openCreateForm();
      fixture.detectChanges();
      expect(el.querySelector('#md-scope')).toBeTruthy();
    });

    it('refuses to create without a scope, and issues no request', () => {
      fixture.detectChanges();
      flushAggregate();

      const comp = fixture.componentInstance;
      comp.selectTab('documentTypes');
      comp.openCreateForm();
      comp.formCode.set('TEST_REPORT');
      comp.formLabel.set('Test Report');
      comp.submitForm();

      expect(comp.formError()).toContain('Scope is required');
      // The point of the story: no silent Vendor-scoped row reaches the server.
      httpMock.expectNone('/api/v1/master-data/document-types');
    });

    it('POSTs { code, label, scope } when a shipment-scoped type is created', () => {
      fixture.detectChanges();
      flushAggregate();

      const comp = fixture.componentInstance;
      comp.selectTab('documentTypes');
      comp.openCreateForm();
      comp.formCode.set('CERT_ORIGIN');
      comp.formLabel.set('Certificate of Origin');
      comp.formScope.set('Shipment');
      comp.submitForm();

      const req = httpMock.expectOne('/api/v1/master-data/document-types');
      expect(req.request.body).toEqual({ code: 'CERT_ORIGIN', label: 'Certificate of Origin', scope: 'Shipment' });
      req.flush(
        { id: 'doc-new', code: 'CERT_ORIGIN', label: 'Certificate of Origin', sortOrder: 6, isActive: true, isSystemDefault: false, scope: 'Shipment' },
        { status: 201, statusText: 'Created' }
      );
      flushAggregate();
    });

    it('renders scope read-only on edit and omits it from the PUT body (the server ignores it there)', () => {
      fixture.detectChanges();
      flushAggregate();
      fixture.detectChanges();

      const comp = fixture.componentInstance;
      comp.selectTab('documentTypes');
      fixture.detectChanges();
      comp.openEditForm(comp.rows().find((r) => r.id === 'doc-2')!);
      fixture.detectChanges();

      const scopeEl = fixture.nativeElement.querySelector('#md-scope') as HTMLSelectElement;
      expect(scopeEl.disabled).toBeTrue();
      expect(comp.formScope()).toBe('Shipment');

      comp.formLabel.set('Packing List (revised)');
      comp.submitForm();

      const req = httpMock.expectOne('/api/v1/master-data/document-types/doc-2');
      expect(req.request.body).toEqual({ code: 'PACKING_LIST', label: 'Packing List (revised)' });
      req.flush({ id: 'doc-2', code: 'PACKING_LIST', label: 'Packing List (revised)', sortOrder: 5, isActive: true, isSystemDefault: true, scope: 'Shipment' });
      flushAggregate();
    });

    it('shows each type\'s scope in the list so the two sets are tellable apart', () => {
      fixture.detectChanges();
      flushAggregate();
      fixture.detectChanges();

      fixture.componentInstance.selectTab('documentTypes');
      fixture.detectChanges();

      const chips = Array.from(fixture.nativeElement.querySelectorAll('.md-scope-chip')).map((c) =>
        (c as HTMLElement).textContent!.trim()
      );
      expect(chips).toEqual(['Vendor', 'Shipment']);
    });
  });

  describe('disabled-code-on-edit rule', () => {
    it('renders the Code input disabled on edit, with a hint explaining why', () => {
      fixture.detectChanges();
      flushAggregate();
      fixture.detectChanges();

      const comp = fixture.componentInstance;
      comp.selectTab('serviceTypes');
      fixture.detectChanges();
      const row = AGGREGATE.serviceTypes[0];
      comp.openEditForm(row);
      fixture.detectChanges();

      const codeInput: HTMLInputElement = fixture.nativeElement.querySelector('#md-code');
      expect(codeInput.disabled).toBeTrue();
      expect(codeInput.value).toBe('CIF');
      expect(fixture.nativeElement.textContent).toContain("Code can't be changed after creation");
    });

    it('leaves the Code input enabled on create', () => {
      fixture.detectChanges();
      flushAggregate();
      fixture.detectChanges();

      const comp = fixture.componentInstance;
      comp.selectTab('serviceTypes');
      comp.openCreateForm();
      fixture.detectChanges();

      const codeInput: HTMLInputElement = fixture.nativeElement.querySelector('#md-code');
      expect(codeInput.disabled).toBeFalse();
    });

    it('still sends code+label on update, but the server never applies a code change (D-12)', () => {
      fixture.detectChanges();
      flushAggregate();

      const comp = fixture.componentInstance;
      comp.selectTab('serviceTypes');
      const row = AGGREGATE.serviceTypes[0];
      comp.openEditForm(row);
      comp.formLabel.set('CIF renamed');
      comp.submitForm();

      const req = httpMock.expectOne('/api/v1/master-data/service-types/svc-1');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ code: 'CIF', label: 'CIF renamed' });
      req.flush({ ...row, label: 'CIF renamed' });
      flushAggregate();
    });
  });

  describe('reorder', () => {
    it('emits the correct [{ id, sortOrder }] payload when moving a row down', () => {
      fixture.detectChanges();
      flushAggregate();
      fixture.detectChanges();

      // categories tab: Jewellery (sortOrder 1) moved down swaps with Handbags (sortOrder 2)
      fixture.componentInstance.moveDown(AGGREGATE.categories[0]);

      const req = httpMock.expectOne('/api/v1/master-data/categories/reorder');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual([
        { id: 'cat-1', sortOrder: 2 },
        { id: 'cat-2', sortOrder: 1 }
      ]);
      req.flush([]);
      flushAggregate();
    });

    it('disables Move up for the first row and Move down for the last row', () => {
      fixture.detectChanges();
      flushAggregate();
      fixture.detectChanges();

      const comp = fixture.componentInstance;
      expect(comp.canMoveUp(AGGREGATE.categories[0])).toBeFalse();
      expect(comp.canMoveDown(AGGREGATE.categories[0])).toBeTrue();
      expect(comp.canMoveDown(AGGREGATE.categories[1])).toBeFalse();
    });

    it('disables reorder for a retired row', () => {
      fixture.detectChanges();
      flushAggregate();
      fixture.detectChanges();
      fixture.componentInstance.toggleIncludeRetired();

      const comp = fixture.componentInstance;
      const retired = AGGREGATE.categories[2];
      expect(comp.canMoveUp(retired)).toBeFalse();
      expect(comp.canMoveDown(retired)).toBeFalse();
    });
  });

  describe('delete', () => {
    it('surfaces the 409 "retire instead of deleting" detail verbatim', () => {
      fixture.detectChanges();
      flushAggregate();
      fixture.detectChanges();

      const comp = fixture.componentInstance;
      comp.deleteRow(AGGREGATE.categories[1]);

      const req = httpMock.expectOne('/api/v1/master-data/categories/cat-2');
      expect(req.request.method).toBe('DELETE');
      req.flush(
        { title: 'Cannot delete a referenced master-data row.', detail: 'This category is referenced by customers. Retire it instead of deleting.' },
        { status: 409, statusText: 'Conflict' }
      );
      fixture.detectChanges();

      expect(comp.actionError()).toBe('This category is referenced by customers. Retire it instead of deleting.');
      expect(fixture.nativeElement.textContent).toContain('Retire it instead of deleting.');
    });

    it('is disabled up front for a seeded (isSystemDefault) row and never calls DELETE', () => {
      fixture.detectChanges();
      flushAggregate();
      fixture.detectChanges();

      const comp = fixture.componentInstance;
      comp.deleteRow(AGGREGATE.categories[0]); // isSystemDefault: true

      httpMock.expectNone('/api/v1/master-data/categories/cat-1');

      const buttons: HTMLButtonElement[] = Array.from(fixture.nativeElement.querySelectorAll('button'));
      const deleteBtn = buttons.find((b) => b.title.includes('retire-only'));
      expect(deleteBtn?.disabled).toBeTrue();
    });
  });

  it('shows a visible error instead of hanging when the aggregate request fails', () => {
    fixture.detectChanges();
    httpMock
      .expectOne((r) => r.url === '/api/v1/master-data')
      .flush({ title: 'Server error', detail: 'Lookup failed' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.componentInstance.loading()).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('Lookup failed');
  });
});
