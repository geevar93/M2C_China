import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminUsersService } from './admin-users.service';
import { AdminUserDto } from '../models/admin-user.models';

describe('AdminUsersService', () => {
  let service: AdminUsersService;
  let httpMock: HttpTestingController;

  const user: AdminUserDto = {
    id: 'user-1',
    name: 'Priya Sharma',
    email: 'priya@sourcingops.test',
    isActive: true,
    mustChangePassword: false,
    roles: ['Associate'],
    createdAt: '2026-02-11T00:00:00Z'
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(AdminUsersService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('sends search/page/pageSize/includeInactive as query params on GET /admin/users', () => {
    service.list({ search: 'priya', page: 2, pageSize: 20, includeInactive: true }).subscribe();

    const req = httpMock.expectOne(
      (r) =>
        r.url === '/api/v1/admin/users' &&
        r.params.get('search') === 'priya' &&
        r.params.get('page') === '2' &&
        r.params.get('pageSize') === '20' &&
        r.params.get('includeInactive') === 'true'
    );
    expect(req.request.method).toBe('GET');
    req.flush({ items: [user], totalCount: 1, page: 2, pageSize: 20 });
  });

  it('omits an empty search from the query string', () => {
    service.list({ page: 1, pageSize: 20, includeInactive: false }).subscribe();
    const req = httpMock.expectOne((r) => r.url === '/api/v1/admin/users');
    expect(req.request.params.has('search')).toBeFalse();
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });

  it('POSTs a create request and returns the one-time temporary password', () => {
    let result: { user: AdminUserDto; temporaryPassword: string } | undefined;
    service.create({ name: 'New Hire', email: 'new@sourcingops.test', roleIds: ['role-1'] }).subscribe((r) => (result = r));

    const req = httpMock.expectOne('/api/v1/admin/users');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'New Hire', email: 'new@sourcingops.test', roleIds: ['role-1'] });
    req.flush({ user, temporaryPassword: 'Temp!23456' }, { status: 201, statusText: 'Created' });

    expect(result?.temporaryPassword).toBe('Temp!23456');
  });

  it('POSTs a reset-password request and returns the one-time temporary password', () => {
    let result: { temporaryPassword: string } | undefined;
    service.resetPassword('user-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne('/api/v1/admin/users/user-1/reset-password');
    expect(req.request.method).toBe('POST');
    req.flush({ temporaryPassword: 'Reset!789' });

    expect(result?.temporaryPassword).toBe('Reset!789');
  });

  it('DELETEs to deactivate a user', () => {
    service.deactivate('user-1').subscribe();
    const req = httpMock.expectOne('/api/v1/admin/users/user-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('surfaces the 400 last-active-Super-Admin ProblemDetails detail on deactivate', () => {
    let caught: unknown;
    service.deactivate('user-1').subscribe({ error: (err) => (caught = err) });

    const req = httpMock.expectOne('/api/v1/admin/users/user-1');
    req.flush(
      { title: 'Cannot deactivate the last active Super Admin.', detail: 'This would leave no active Super Admin.' },
      { status: 400, statusText: 'Bad Request' }
    );

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect((caught as any).error.detail).toBe('This would leave no active Super Admin.');
  });

  it('POSTs to restore a deactivated user', () => {
    service.restore('user-1').subscribe();
    const req = httpMock.expectOne('/api/v1/admin/users/user-1/restore');
    expect(req.request.method).toBe('POST');
    req.flush(user);
  });

  it('PUTs role ids to assign roles', () => {
    service.assignRoles('user-1', ['role-1', 'role-2']).subscribe();
    const req = httpMock.expectOne('/api/v1/admin/users/user-1/roles');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ roleIds: ['role-1', 'role-2'] });
    req.flush(user);
  });

  it('gets the role list', () => {
    service.listRoles().subscribe();
    const req = httpMock.expectOne('/api/v1/admin/roles');
    expect(req.request.method).toBe('GET');
    req.flush([{ id: 'role-1', name: 'Associate' }]);
  });
});
