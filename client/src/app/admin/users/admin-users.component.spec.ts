import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { AdminUsersComponent } from './admin-users.component';
import { AdminUserDto } from '../models/admin-user.models';

function user(overrides: Partial<AdminUserDto> = {}): AdminUserDto {
  return {
    id: 'user-1',
    name: 'Priya Sharma',
    email: 'priya@sourcingops.test',
    isActive: true,
    mustChangePassword: false,
    roles: ['Associate'],
    createdAt: '2026-02-11T00:00:00Z',
    ...overrides
  };
}

describe('AdminUsersComponent', () => {
  let fixture: ComponentFixture<AdminUsersComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminUsersComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    fixture = TestBed.createComponent(AdminUsersComponent);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function flushList(items: AdminUserDto[], totalCount = items.length): void {
    httpMock.expectOne((r) => r.url === '/api/v1/admin/users').flush({ items, totalCount, page: 1, pageSize: 20 });
  }

  it('renders active and inactive users, each with their status chip', () => {
    fixture.detectChanges();
    flushList([user({ id: 'u-1', name: 'Priya Sharma', isActive: true }), user({ id: 'u-2', name: 'Old Hire', isActive: false })]);
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Priya Sharma');
    expect(text).toContain('Old Hire');
    expect(text).toContain('Active');
    expect(text).toContain('Inactive');
  });

  it('shows a "Must change password" chip only for users with mustChangePassword true', () => {
    fixture.detectChanges();
    flushList([user({ mustChangePassword: true })]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Must change password');
  });

  it('sends includeInactive as a query param when "Show inactive" is toggled', () => {
    fixture.detectChanges();
    flushList([]);
    fixture.detectChanges();

    fixture.componentInstance.toggleIncludeInactive();

    const req = httpMock.expectOne((r) => r.url === '/api/v1/admin/users');
    expect(req.request.params.get('includeInactive')).toBe('true');
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });

  it('debounces the search box and sends it as the search query param', fakeAsync(() => {
    fixture.detectChanges();
    flushList([]);
    fixture.detectChanges();

    fixture.componentInstance.onSearchInput('priya');
    tick(350);

    const req = httpMock.expectOne((r) => r.url === '/api/v1/admin/users');
    expect(req.request.params.get('search')).toBe('priya');
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  }));

  it('shows a visible error instead of hanging when the list request fails', () => {
    fixture.detectChanges();
    httpMock
      .expectOne((r) => r.url === '/api/v1/admin/users')
      .flush({ title: 'Server error', detail: 'Lookup failed' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.componentInstance.loading()).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('Lookup failed');
  });

  describe('one-time temporary password modal', () => {
    it('appears after a user is created, showing the password from the response', () => {
      fixture.detectChanges();
      flushList([]);
      fixture.detectChanges();

      const comp = fixture.componentInstance;
      comp.openCreate();
      const rolesReq = httpMock.expectOne((r) => r.url === '/api/v1/admin/roles');
      rolesReq.flush([]);

      comp.createName.set('New Hire');
      comp.createEmail.set('new@sourcingops.test');
      comp.submitCreate();

      const createReq = httpMock.expectOne((r) => r.url === '/api/v1/admin/users' && r.method === 'POST');
      createReq.flush(
        { user: user({ id: 'u-new', name: 'New Hire' }), temporaryPassword: 'Temp!23456' },
        { status: 201, statusText: 'Created' }
      );
      flushList([user({ id: 'u-new', name: 'New Hire' })]);
      fixture.detectChanges();

      expect(comp.tempPassword()).toBe('Temp!23456');
      expect(fixture.nativeElement.textContent).toContain('Temp!23456');
      expect(fixture.nativeElement.textContent).toContain('only be shown this one time');
    });

    it('appears after a password reset, showing the password from the response', () => {
      fixture.detectChanges();
      flushList([user()]);
      fixture.detectChanges();

      fixture.componentInstance.resetPassword(user());
      const resetReq = httpMock.expectOne((r) => r.url === '/api/v1/admin/users/user-1/reset-password');
      resetReq.flush({ temporaryPassword: 'Reset!789' });
      flushList([user()]);
      fixture.detectChanges();

      expect(fixture.componentInstance.tempPassword()).toBe('Reset!789');
    });

    it('has no backdrop (click) handler and no Escape binding — only acknowledgeTempPassword() closes it', () => {
      fixture.detectChanges();
      flushList([user()]);
      fixture.detectChanges();

      const comp = fixture.componentInstance;
      comp.resetPassword(user());
      httpMock.expectOne((r) => r.url === '/api/v1/admin/users/user-1/reset-password').flush({ temporaryPassword: 'Reset!789' });
      flushList([user()]);
      fixture.detectChanges();

      const backdrop: HTMLElement = fixture.nativeElement.querySelector('.dialog-backdrop');
      expect(backdrop).toBeTruthy();

      // Simulate a backdrop click and an Escape keypress — neither is wired
      // to any handler, so the password must still be showing afterwards.
      backdrop.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      fixture.detectChanges();

      expect(comp.tempPassword()).toBe('Reset!789');
      expect(fixture.nativeElement.textContent).toContain('Reset!789');

      comp.acknowledgeTempPassword();
      fixture.detectChanges();
      expect(comp.tempPassword()).toBeNull();
    });
  });

  describe('deactivate confirmation', () => {
    it('requires confirmation before calling DELETE', () => {
      fixture.detectChanges();
      flushList([user()]);
      fixture.detectChanges();

      const comp = fixture.componentInstance;
      comp.openDeactivateConfirm(user());
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain('Deactivate Priya Sharma?');

      httpMock.expectNone((r) => r.url === '/api/v1/admin/users/user-1');

      comp.confirmDeactivate();
      const req = httpMock.expectOne((r) => r.url === '/api/v1/admin/users/user-1' && r.method === 'DELETE');
      req.flush(null, { status: 204, statusText: 'No Content' });
      flushList([user({ isActive: false })]);
    });

    it('surfaces the last-active-Super-Admin 400 ProblemDetails detail verbatim', () => {
      fixture.detectChanges();
      flushList([user()]);
      fixture.detectChanges();

      const comp = fixture.componentInstance;
      comp.openDeactivateConfirm(user());
      comp.confirmDeactivate();

      const req = httpMock.expectOne((r) => r.url === '/api/v1/admin/users/user-1');
      req.flush(
        { title: 'Cannot deactivate.', detail: 'This would leave no active Super Admin.' },
        { status: 400, statusText: 'Bad Request' }
      );
      fixture.detectChanges();

      expect(comp.deactivateError()).toBe('This would leave no active Super Admin.');
      expect(fixture.nativeElement.textContent).toContain('This would leave no active Super Admin.');
    });
  });

  describe('roles modal', () => {
    it('states that the user must sign in again for a role change to take effect', () => {
      fixture.detectChanges();
      flushList([user()]);
      fixture.detectChanges();

      fixture.componentInstance.openRoles(user());
      httpMock.expectOne((r) => r.url === '/api/v1/admin/roles').flush([{ id: 'role-1', name: 'Associate' }]);
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('sign in again');
    });

    it('resolves the role-name list on the user to role ids for the checkbox state', () => {
      fixture.detectChanges();
      flushList([user({ roles: ['Associate'] })]);
      fixture.detectChanges();

      const comp = fixture.componentInstance;
      comp.openRoles(user({ roles: ['Associate'] }));
      httpMock.expectOne((r) => r.url === '/api/v1/admin/roles').flush([
        { id: 'role-assoc', name: 'Associate' },
        { id: 'role-admin', name: 'Super Admin' }
      ]);

      expect(comp.roleModalSelectedIds()).toEqual(['role-assoc']);
    });

    it('PUTs the selected role ids', () => {
      fixture.detectChanges();
      flushList([user()]);
      fixture.detectChanges();

      const comp = fixture.componentInstance;
      comp.openRoles(user());
      httpMock.expectOne((r) => r.url === '/api/v1/admin/roles').flush([{ id: 'role-assoc', name: 'Associate' }]);
      comp.toggleRoleSelection('role-admin');
      comp.saveRoles();

      const req = httpMock.expectOne((r) => r.url === '/api/v1/admin/users/user-1/roles');
      expect(req.request.body).toEqual({ roleIds: ['role-assoc', 'role-admin'] });
      req.flush(user());
      flushList([user()]);
    });
  });
});
