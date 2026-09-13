import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { CommandPaletteComponent } from './command-palette.component';
import { AuthService } from '../services/auth.service';
import { CustomersService } from '../../customers/services/customers.service';
import { VendorsService } from '../../vendors/services/vendors.service';
import { CatalogsService } from '../../catalogs/services/catalogs.service';
import { ShipmentsService } from '../../shipments/services/shipments.service';
import { InventoryService } from '../../inventory/services/inventory.service';

describe('CommandPaletteComponent', () => {
  let fixture: ComponentFixture<CommandPaletteComponent>;
  let customersList: jasmine.Spy;
  let vendorsList: jasmine.Spy;

  const emptyPage = () => of({ items: [], page: 1, pageSize: 5, totalCount: 0 });

  async function configure(permissions: string[]): Promise<void> {
    customersList = jasmine.createSpy('customers.list').and.returnValue(
      of({
        items: [{ id: 'c1', name: 'Meena Shah', businessName: 'Meena Traders', phone: '+91 9', city: 'Surat' }],
        page: 1,
        pageSize: 5,
        totalCount: 1
      })
    );
    vendorsList = jasmine.createSpy('vendors.list').and.returnValue(throwError(() => new Error('boom')));
    await TestBed.configureTestingModule({
      imports: [CommandPaletteComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p) } },
        { provide: CustomersService, useValue: { list: customersList } },
        { provide: VendorsService, useValue: { list: vendorsList } },
        { provide: CatalogsService, useValue: { list: emptyPage } },
        { provide: ShipmentsService, useValue: { list: emptyPage } },
        { provide: InventoryService, useValue: { list: emptyPage } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(CommandPaletteComponent);
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  it('lists only the screens the user may open when the query is empty', async () => {
    await configure(['Customers.View', 'Vendors.View']);
    const labels = fixture.componentInstance.results().map((r) => r.label);
    expect(labels).toContain('Customers');
    expect(labels).toContain('Vendors');
    expect(labels).not.toContain('Dashboard');
    expect(labels).not.toContain('Users');
  });

  it('fans the query out to permitted sources and survives a failing one', fakeAsync(async () => {
    await configure(['Customers.View', 'Vendors.View']);
    fixture.componentInstance.onInput('meena');
    tick(300);
    fixture.detectChanges();

    expect(customersList).toHaveBeenCalledWith({ search: 'meena', page: 1, pageSize: 5 });
    expect(vendorsList).toHaveBeenCalled();
    const hit = fixture.componentInstance.results().find((r) => r.kind === 'Customer');
    expect(hit?.label).toBe('Meena Traders');
    expect(hit?.route).toEqual(['/customers', 'c1']);
  }));

  it('skips sources the user cannot view', fakeAsync(async () => {
    await configure(['Vendors.View']);
    fixture.componentInstance.onInput('x');
    tick(300);
    expect(customersList).not.toHaveBeenCalled();
  }));

  it('navigates to the active result on Enter and emits closed', async () => {
    await configure(['Customers.View']);
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    let closed = 0;
    fixture.componentInstance.closed.subscribe(() => closed++);

    fixture.componentInstance.onKeydown(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(navigate).toHaveBeenCalledWith(['/customers'], { queryParams: undefined });
    expect(closed).toBe(1);
  });
});
