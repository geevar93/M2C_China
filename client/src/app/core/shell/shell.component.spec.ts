import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ShellComponent } from './shell.component';
import { AuthService } from '../services/auth.service';
import { MasterDataService } from '../services/master-data.service';
import { RefreshService } from '../services/refresh.service';

describe('ShellComponent', () => {
  let fixture: ComponentFixture<ShellComponent>;
  let masterDataReloads: number;

  async function configure(permissions: string[]): Promise<void> {
    masterDataReloads = 0;
    await TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            hasPermission: (p: string) => permissions.includes(p),
            currentUser$: of({ id: 'u1', name: 'Test User', email: 'user@m2c.example', roles: ['SuperAdmin'], permissions }),
            logout: () => undefined
          }
        },
        {
          provide: MasterDataService,
          useValue: {
            reload: () => {
              masterDataReloads++;
              return of(null);
            }
          }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ShellComponent);
    fixture.detectChanges();
  }

  function openUserMenu(): void {
    (fixture.nativeElement.querySelector('.topbar__user-btn') as HTMLButtonElement).click();
    fixture.detectChanges();
  }

  function accountLink(): HTMLAnchorElement | null {
    return fixture.nativeElement.querySelector('.topbar__account-link');
  }

  afterEach(() => TestBed.resetTestingModule());

  it('hides the Change password control without Account.ChangeOwnPassword', async () => {
    await configure(['Analytics.View']);
    openUserMenu();

    expect(fixture.componentInstance.canChangeOwnPassword()).toBeFalse();
    expect(accountLink()).toBeNull();
  });

  it('shows the Change password control, routed to /account/password, with the permission', async () => {
    await configure(['Analytics.View', 'Account.ChangeOwnPassword']);
    openUserMenu();

    expect(fixture.componentInstance.canChangeOwnPassword()).toBeTrue();
    const link = accountLink();
    expect(link).toBeTruthy();
    expect(link!.textContent).toContain('Change password');
    expect(link!.getAttribute('href')).toBe('/account/password');
  });

  it('keeps the log out control regardless of the password permission', async () => {
    await configure([]);
    openUserMenu();
    expect(fixture.nativeElement.querySelector('.topbar__logout')).toBeTruthy();
  });

  it('broadcasts the topbar refresh to the registered screen and reloads master data', async () => {
    await configure(['Analytics.View']);
    const refreshService = TestBed.inject(RefreshService);
    let screenReloads = 0;
    const sub = refreshService.refresh$.subscribe(() => screenReloads++);

    (fixture.nativeElement.querySelector('.topbar__refresh') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(screenReloads).toBe(1);
    expect(masterDataReloads).toBe(1);
    expect(refreshService.busy()).toBeTrue();
    sub.unsubscribe();
  });

  it('opens and closes the mobile navigation drawer', async () => {
    await configure(['Analytics.View']);
    (fixture.nativeElement.querySelector('.topbar__menu') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.shell--mobile-nav-open')).toBeTruthy();

    (fixture.nativeElement.querySelector('.sidebar__close') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.shell--mobile-nav-open')).toBeNull();
  });
});
