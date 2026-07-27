import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ForceChangePasswordComponent } from './force-change-password.component';

describe('ForceChangePasswordComponent', () => {
  let fixture: ComponentFixture<ForceChangePasswordComponent>;
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ForceChangePasswordComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(ForceChangePasswordComponent);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    fixture.detectChanges();
  });

  afterEach(() => httpMock.verify());

  function fillForm(current: string, next: string, confirm: string): void {
    const c = fixture.componentInstance.form.controls;
    c.currentPassword.setValue(current);
    c.newPassword.setValue(next);
    c.confirmPassword.setValue(confirm);
  }

  it('is invalid until the current password is present and the new password meets the minimum length', () => {
    expect(fixture.componentInstance.form.valid).toBeFalse();

    fillForm('temp-pass', 'short', 'short');
    expect(fixture.componentInstance.form.valid).toBeFalse();

    fillForm('temp-pass', 'longenough1', 'longenough1');
    expect(fixture.componentInstance.form.valid).toBeTrue();
  });

  it('flags a mismatch between new and confirm as a field-level error on Confirm, not a banner', () => {
    fillForm('temp-pass', 'longenough1', 'somethingelse');
    fixture.componentInstance.form.controls.confirmPassword.markAsTouched();
    fixture.detectChanges();

    expect(fixture.componentInstance.form.errors?.['mismatch']).toBeTrue();

    const confirmField: HTMLElement = fixture.nativeElement.querySelector('#confirmPassword').closest('.field');
    expect(confirmField.querySelector('.field-error')?.textContent).toContain('Passwords do not match');

    // The mismatch must not be rendered as a banner-warning (that region is reserved
    // for the persistent notice + the API-error banner, per E0-03's design decisions).
    const banners: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.banner-warning'));
    for (const banner of banners) {
      expect(banner.textContent).not.toContain('do not match');
    }

    fixture.componentInstance.submit();
    httpMock.expectNone((r) => r.url === '/api/v1/auth/change-password');
  });

  it('shows the real server-side password policy as a field-hint before submission', () => {
    const hint: HTMLElement = fixture.nativeElement.querySelector('#newPassword').closest('.field').querySelector('.field-hint');
    expect(hint).toBeTruthy();
    expect(hint.textContent).toContain('at least 8 characters');
  });

  it('renders a persistent banner-warning stating no other screen is reachable until this is done', () => {
    const banners: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.banner-warning'));
    const notice = banners.find((b) => b.textContent?.toLowerCase().includes('no other screen'));
    expect(notice).toBeTruthy();
  });

  it('disables the submit button and swaps its label while the request is in flight', () => {
    fillForm('temp-pass', 'longenough1', 'longenough1');
    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(fixture.componentInstance.loading()).toBeTrue();
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('button[type="submit"]');
    expect(button.disabled).toBeTrue();
    expect(button.textContent).toContain('Updating…');

    httpMock.expectOne((r) => r.url === '/api/v1/auth/change-password').flush(
      { title: 'Current password is incorrect.' },
      { status: 400, statusText: 'Bad Request' }
    );
  });

  it('shows an API error as a role="alert" banner-warning', () => {
    fillForm('wrong-temp-pass', 'longenough1', 'longenough1');
    fixture.componentInstance.submit();

    httpMock.expectOne((r) => r.url === '/api/v1/auth/change-password').flush(
      { title: 'Current password is incorrect.' },
      { status: 400, statusText: 'Bad Request' }
    );
    fixture.detectChanges();

    expect(fixture.componentInstance.errorMessage()).toBe('Current password is incorrect.');
    const alertBanner: HTMLElement = fixture.nativeElement.querySelector('.banner-warning[role="alert"]');
    expect(alertBanner).toBeTruthy();
    expect(alertBanner.textContent).toContain('Current password is incorrect.');
  });

  it('navigates straight to the dashboard on success, not back to login', () => {
    const navigateSpy = spyOn(router, 'navigate');
    fillForm('temp-pass', 'longenough1', 'longenough1');
    fixture.componentInstance.submit();

    httpMock.expectOne((r) => r.url === '/api/v1/auth/change-password').flush({
      accessToken: 'a.b.c',
      refreshToken: 'r',
      expiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
      mustChangePassword: false,
      user: { id: 'u1', name: 'Test User', email: 'user@meridian.example', roles: [], permissions: [] }
    });

    expect(navigateSpy).toHaveBeenCalledWith(['/dashboard']);
  });
});
