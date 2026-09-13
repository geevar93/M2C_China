import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ChangePasswordComponent } from './change-password.component';

const CHANGE_PASSWORD_URL = '/api/v1/auth/change-password';

function authResponse() {
  return {
    accessToken: 'a.b.c',
    refreshToken: 'r',
    expiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
    mustChangePassword: false,
    user: { id: 'u1', name: 'Test User', email: 'user@m2c.example', roles: ['SuperAdmin'], permissions: [] }
  };
}

describe('ChangePasswordComponent', () => {
  let fixture: ComponentFixture<ChangePasswordComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ChangePasswordComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(ChangePasswordComponent);
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    localStorage.removeItem('sop.auth.v1');
    httpMock.verify();
  });

  function fillForm(current: string, next: string, confirm: string): void {
    const c = fixture.componentInstance.form.controls;
    c.currentPassword.setValue(current);
    c.newPassword.setValue(next);
    c.confirmPassword.setValue(confirm);
  }

  it('submits the current and new password and reports success inline', () => {
    fillForm('old-pass-1', 'longenough1', 'longenough1');
    fixture.componentInstance.submit();

    const req = httpMock.expectOne((r) => r.url === CHANGE_PASSWORD_URL);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ currentPassword: 'old-pass-1', newPassword: 'longenough1' });
    req.flush(authResponse());
    fixture.detectChanges();

    expect(fixture.componentInstance.loading()).toBeFalse();
    expect(fixture.componentInstance.errorMessage()).toBeNull();
    const status: HTMLElement = fixture.nativeElement.querySelector('[role="status"]');
    expect(status).toBeTruthy();
    expect(status.textContent).toContain('Password changed');
  });

  it('clears the form after a successful change', () => {
    fillForm('old-pass-1', 'longenough1', 'longenough1');
    fixture.componentInstance.submit();
    httpMock.expectOne((r) => r.url === CHANGE_PASSWORD_URL).flush(authResponse());

    const c = fixture.componentInstance.form.controls;
    expect(c.currentPassword.value).toBe('');
    expect(c.newPassword.value).toBe('');
    expect(c.confirmPassword.value).toBe('');
    expect(fixture.componentInstance.form.untouched).toBeTrue();
  });

  it('does not submit when the new password is shorter than the minimum', () => {
    fillForm('old-pass-1', 'short', 'short');
    expect(fixture.componentInstance.form.valid).toBeFalse();

    fixture.componentInstance.submit();
    httpMock.expectNone((r) => r.url === CHANGE_PASSWORD_URL);
  });

  it('does not submit when the confirmation does not match, and says so on the field', () => {
    fillForm('old-pass-1', 'longenough1', 'somethingelse');
    fixture.componentInstance.form.controls.confirmPassword.markAsTouched();
    fixture.detectChanges();

    expect(fixture.componentInstance.form.errors?.['mismatch']).toBeTrue();
    const confirmField: HTMLElement = fixture.nativeElement.querySelector('#confirmPassword').closest('.field');
    expect(confirmField.querySelector('.field-error')?.textContent).toContain('Passwords do not match');

    fixture.componentInstance.submit();
    httpMock.expectNone((r) => r.url === CHANGE_PASSWORD_URL);
  });

  it('does not submit when the new password is the same as the current one', () => {
    fillForm('longenough1', 'longenough1', 'longenough1');
    fixture.componentInstance.form.controls.newPassword.markAsTouched();
    fixture.detectChanges();

    expect(fixture.componentInstance.form.errors?.['unchanged']).toBeTrue();
    const newField: HTMLElement = fixture.nativeElement.querySelector('#newPassword').closest('.field');
    expect(newField.textContent).toContain("Choose a password you aren't already using");

    fixture.componentInstance.submit();
    httpMock.expectNone((r) => r.url === CHANGE_PASSWORD_URL);
  });

  it("surfaces the server's message when the current password is wrong (400)", () => {
    fillForm('wrong-pass', 'longenough1', 'longenough1');
    fixture.componentInstance.submit();

    httpMock
      .expectOne((r) => r.url === CHANGE_PASSWORD_URL)
      .flush({ title: 'Current password is incorrect.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(fixture.componentInstance.errorMessage()).toBe('Current password is incorrect.');
    const alert: HTMLElement = fixture.nativeElement.querySelector('.banner-warning[role="alert"]');
    expect(alert).toBeTruthy();
    expect(alert.textContent).toContain('Current password is incorrect.');
    expect(fixture.nativeElement.querySelector('[role="status"]')).toBeNull();

    // The form keeps its values on failure — the user only needs to fix one field.
    expect(fixture.componentInstance.form.controls.newPassword.value).toBe('longenough1');
  });

  it('disables the submit button while the request is in flight', () => {
    fillForm('old-pass-1', 'longenough1', 'longenough1');
    fixture.componentInstance.submit();
    fixture.detectChanges();

    const button: HTMLButtonElement = fixture.nativeElement.querySelector('button[type="submit"]');
    expect(button.disabled).toBeTrue();
    expect(button.textContent).toContain('Updating…');

    httpMock.expectOne((r) => r.url === CHANGE_PASSWORD_URL).flush(authResponse());
  });

  it('does not render the forced-flow lockout warning — this screen is voluntary', () => {
    expect(fixture.nativeElement.textContent.toLowerCase()).not.toContain('no other screen');
  });
});
