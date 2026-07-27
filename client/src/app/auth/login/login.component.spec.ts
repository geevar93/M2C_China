import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    fixture.detectChanges();
  });

  afterEach(() => httpMock.verify());

  function setEmail(value: string): void {
    fixture.componentInstance.form.controls.email.setValue(value);
  }

  function setPassword(value: string): void {
    fixture.componentInstance.form.controls.password.setValue(value);
  }

  it('is invalid until both a well-formed email and a password are entered', () => {
    expect(fixture.componentInstance.form.valid).toBeFalse();

    setEmail('not-an-email');
    setPassword('secret123');
    expect(fixture.componentInstance.form.valid).toBeFalse();

    setEmail('user@meridian.example');
    expect(fixture.componentInstance.form.valid).toBeTrue();
  });

  it('does not submit while the form is invalid, and marks fields touched so errors show', () => {
    fixture.componentInstance.submit();
    httpMock.expectNone((r) => r.url === '/api/v1/auth/login');
    expect(fixture.componentInstance.form.controls.email.touched).toBeTrue();
    expect(fixture.componentInstance.form.controls.password.touched).toBeTrue();
  });

  it('disables the submit button and swaps its label while the request is in flight', () => {
    setEmail('user@meridian.example');
    setPassword('secret123');
    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(fixture.componentInstance.loading()).toBeTrue();
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('button[type="submit"]');
    expect(button.disabled).toBeTrue();
    expect(button.textContent).toContain('Signing in…');

    httpMock.expectOne((r) => r.url === '/api/v1/auth/login').flush(
      { title: 'Invalid email or password.' },
      { status: 401, statusText: 'Unauthorized' }
    );
  });

  it('shows the same generic error message whether the email is unknown or the password is wrong', () => {
    setEmail('unknown@meridian.example');
    setPassword('wrong-password');
    fixture.componentInstance.submit();

    httpMock.expectOne((r) => r.url === '/api/v1/auth/login').flush(
      { title: 'Invalid email or password.' },
      { status: 401, statusText: 'Unauthorized' }
    );
    fixture.detectChanges();

    expect(fixture.componentInstance.errorMessage()).toBe('Invalid email or password.');
    const banner: HTMLElement = fixture.nativeElement.querySelector('.banner-warning');
    expect(banner).toBeTruthy();
    expect(banner.getAttribute('role')).toBe('alert');
    expect(banner.textContent).toContain('Invalid email or password.');

    // A second attempt with a different (but still wrong) password must render
    // the identical banner text — the client must not add any account-existence hint.
    setPassword('another-wrong-password');
    fixture.componentInstance.submit();
    httpMock.expectOne((r) => r.url === '/api/v1/auth/login').flush(
      { title: 'Invalid email or password.' },
      { status: 401, statusText: 'Unauthorized' }
    );
    fixture.detectChanges();
    expect(fixture.componentInstance.errorMessage()).toBe('Invalid email or password.');
  });

  it('routes to force-change-password when the API reports mustChangePassword', () => {
    const navigateSpy = spyOn(router, 'navigate');
    setEmail('user@meridian.example');
    setPassword('temp-password');
    fixture.componentInstance.submit();

    httpMock.expectOne((r) => r.url === '/api/v1/auth/login').flush({
      accessToken: 'a.b.c',
      refreshToken: 'r',
      expiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
      mustChangePassword: true,
      user: { id: 'u1', name: 'Test User', email: 'user@meridian.example', roles: [], permissions: [] }
    });

    expect(navigateSpy).toHaveBeenCalledWith(['/force-change-password']);
  });

  it('routes to the dashboard when mustChangePassword is false', () => {
    const navigateSpy = spyOn(router, 'navigate');
    setEmail('user@meridian.example');
    setPassword('correct-password');
    fixture.componentInstance.submit();

    httpMock.expectOne((r) => r.url === '/api/v1/auth/login').flush({
      accessToken: 'a.b.c',
      refreshToken: 'r',
      expiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
      mustChangePassword: false,
      user: { id: 'u1', name: 'Test User', email: 'user@meridian.example', roles: [], permissions: [] }
    });

    expect(navigateSpy).toHaveBeenCalledWith(['/dashboard']);
  });

  it('renders no "forgot password" link and no "remember me" control', () => {
    const text: string = fixture.nativeElement.textContent;
    expect(text.toLowerCase()).not.toContain('forgot password');
    expect(text.toLowerCase()).not.toContain('remember me');
    expect(fixture.nativeElement.querySelector('input[type="checkbox"]')).toBeNull();
  });
});
