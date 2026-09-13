import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CompanySettings } from '../../invoices/models/invoice.models';
import { AdminCompanySettingsComponent } from './admin-company-settings.component';

/** The all-null shape the backend returns before the singleton has ever been saved. */
const UNCONFIGURED: CompanySettings = {
  legalEntityName: null,
  gstin: null,
  stateCode: null,
  stateName: null,
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

const CONFIGURED: CompanySettings = {
  ...UNCONFIGURED,
  legalEntityName: 'M2C Sourcing Pvt Ltd',
  registeredAddress: '123 Industrial Estate, Surat, Gujarat',
  gstin: '24AAAAA0000A1Z5',
  stateCode: '24',
  stateName: 'Gujarat',
  invoiceNumberPrefix: 'INV',
  updatedAt: '2026-09-12T10:00:00Z',
  updatedByName: 'Test Admin'
};

describe('AdminCompanySettingsComponent', () => {
  let fixture: ComponentFixture<AdminCompanySettingsComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminCompanySettingsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    fixture = TestBed.createComponent(AdminCompanySettingsComponent);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function flushGet(body: CompanySettings): void {
    httpMock.expectOne((r) => r.url === '/api/v1/admin/company-settings' && r.method === 'GET').flush(body);
  }

  function setSelect(id: string, value: string): void {
    const el = fixture.nativeElement.querySelector(id) as HTMLSelectElement;
    el.value = value;
    el.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function setInput(id: string, value: string): void {
    const el = fixture.nativeElement.querySelector(id) as HTMLInputElement | HTMLTextAreaElement;
    el.value = value;
    el.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('warns that invoicing is blocked and names both missing fields when nothing is configured', () => {
    fixture.detectChanges();
    flushGet(UNCONFIGURED);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Invoicing is blocked until this is filled in.');
    expect(text).toContain('Legal entity name and Registered address and GST state');
    expect(text).toContain('Never saved.');
  });

  it('hides the blocked banner once both gate fields are filled', () => {
    fixture.detectChanges();
    flushGet(UNCONFIGURED);
    fixture.detectChanges();

    setInput('#cs-legal-name', 'M2C Sourcing Pvt Ltd');
    setInput('#cs-address', '123 Industrial Estate, Surat, Gujarat');
    setSelect('#cs-state', '24');

    expect(fixture.nativeElement.textContent).not.toContain('Invoicing is blocked');
  });

  it('populates every field from the saved row and shows who last updated it', () => {
    fixture.detectChanges();
    flushGet(CONFIGURED);
    fixture.detectChanges();

    expect((fixture.nativeElement.querySelector('#cs-legal-name') as HTMLInputElement).value).toBe('M2C Sourcing Pvt Ltd');
    expect((fixture.nativeElement.querySelector('#cs-gstin') as HTMLInputElement).value).toBe('24AAAAA0000A1Z5');
    expect(fixture.nativeElement.textContent).toContain('Test Admin');
  });

  it('PUTs every field, sending blanks as null rather than empty strings', () => {
    fixture.detectChanges();
    flushGet(UNCONFIGURED);
    fixture.detectChanges();

    setInput('#cs-legal-name', '  M2C Sourcing Pvt Ltd  ');
    setInput('#cs-address', '123 Industrial Estate, Surat, Gujarat');
    setSelect('#cs-state', '24');
    setInput('#cs-prefix', '   ');

    (fixture.nativeElement.querySelector('.btn-primary') as HTMLButtonElement).click();

    const req = httpMock.expectOne((r) => r.url === '/api/v1/admin/company-settings' && r.method === 'PUT');
    expect(req.request.body.legalEntityName).toBe('M2C Sourcing Pvt Ltd');
    expect(req.request.body.invoiceNumberPrefix).toBeNull();
    expect(req.request.body.gstin).toBeNull();

    req.flush(CONFIGURED);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Company settings saved.');
  });

  it('refuses to save while a gate field is blank, without issuing a request', () => {
    fixture.detectChanges();
    flushGet(UNCONFIGURED);
    fixture.detectChanges();

    setInput('#cs-legal-name', 'M2C Sourcing Pvt Ltd');
    (fixture.nativeElement.querySelector('.btn-primary') as HTMLButtonElement).click();
    fixture.detectChanges();

    httpMock.expectNone((r) => r.method === 'PUT');
    expect(fixture.nativeElement.textContent).toContain(
      'Legal entity name, registered address and GST state are all required'
    );
  });

  it('surfaces a ProblemDetails detail from a failed save', () => {
    fixture.detectChanges();
    flushGet(UNCONFIGURED);
    fixture.detectChanges();

    setInput('#cs-legal-name', 'M2C Sourcing Pvt Ltd');
    setInput('#cs-address', '123 Industrial Estate, Surat, Gujarat');
    setSelect('#cs-state', '24');
    (fixture.nativeElement.querySelector('.btn-primary') as HTMLButtonElement).click();

    httpMock
      .expectOne((r) => r.method === 'PUT')
      .flush({ detail: 'GSTIN is not valid.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('GSTIN is not valid.');
  });

  it('fills the state from a typed GSTIN, but never overwrites a hand-picked one', () => {
    fixture.detectChanges();
    flushGet(UNCONFIGURED);
    fixture.detectChanges();

    setInput('#cs-gstin', '27ABCDE1234F1Z5');
    expect((fixture.nativeElement.querySelector('#cs-state') as HTMLSelectElement).value).toBe('27');

    // Hand-picked now: a later GSTIN edit must leave it alone.
    setSelect('#cs-state', '24');
    setInput('#cs-gstin', '29ABCDE1234F1Z5');
    expect((fixture.nativeElement.querySelector('#cs-state') as HTMLSelectElement).value).toBe('24');
  });

  it('offers a retry when the initial load fails', () => {
    fixture.detectChanges();
    httpMock
      .expectOne((r) => r.method === 'GET')
      .flush({ detail: 'Boom' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Boom');
    (fixture.nativeElement.querySelector('.btn-secondary') as HTMLButtonElement).click();
    flushGet(UNCONFIGURED);
  });
});
