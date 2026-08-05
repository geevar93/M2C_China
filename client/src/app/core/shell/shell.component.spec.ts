import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ShellComponent } from './shell.component';
import { AuthService } from '../services/auth.service';

describe('ShellComponent', () => {
  let fixture: ComponentFixture<ShellComponent>;

  async function configure(permissions: string[]): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            hasPermission: (p: string) => permissions.includes(p),
            currentUser$: of({ id: 'u1', name: 'Test User', email: 'user@meridian.example', roles: ['SuperAdmin'], permissions }),
            logout: () => undefined
          }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ShellComponent);
    fixture.detectChanges();
  }

  function accountLink(): HTMLAnchorElement | null {
    return fixture.nativeElement.querySelector('.topbar__account-link');
  }

  afterEach(() => TestBed.resetTestingModule());

  it('hides the Change password control without Account.ChangeOwnPassword', async () => {
    await configure(['Analytics.View']);

    expect(fixture.componentInstance.canChangeOwnPassword()).toBeFalse();
    expect(accountLink()).toBeNull();
  });

  it('shows the Change password control, routed to /account/password, with the permission', async () => {
    await configure(['Analytics.View', 'Account.ChangeOwnPassword']);

    expect(fixture.componentInstance.canChangeOwnPassword()).toBeTrue();
    const link = accountLink();
    expect(link).toBeTruthy();
    expect(link!.textContent).toContain('Change password');
    expect(link!.getAttribute('href')).toBe('/account/password');
  });

  it('keeps the log out control regardless of the password permission', async () => {
    await configure([]);
    expect(fixture.nativeElement.querySelector('.topbar__logout')).toBeTruthy();
  });
});
