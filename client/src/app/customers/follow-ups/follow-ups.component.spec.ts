import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { FollowUpsComponent } from './follow-ups.component';
import { DueFollowUp } from '../models/customer.models';

function isoDaysAgo(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() - days);
  return d.toISOString();
}

const DUE_TODAY: DueFollowUp = {
  interactionId: 'int-today',
  customerId: 'cust-today',
  customerName: 'Meena Traders',
  text: 'Call back about pricing',
  followUpDate: isoDaysAgo(0),
  authorName: 'Priya Sharma'
};

const OVERDUE: DueFollowUp = {
  interactionId: 'int-overdue',
  customerId: 'cust-overdue',
  customerName: 'Anil Exports',
  text: 'Send revised quote',
  followUpDate: isoDaysAgo(3),
  authorName: 'Rahul Mehta'
};

describe('FollowUpsComponent', () => {
  let fixture: ComponentFixture<FollowUpsComponent>;
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FollowUpsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(FollowUpsComponent);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => httpMock.verify());

  function flush(items: DueFollowUp[]): void {
    httpMock.expectOne((r) => r.url === '/api/v1/customers/follow-ups/due').flush(items);
  }

  it('renders a row per due follow-up, preserving the earliest-first order the backend returns', () => {
    fixture.detectChanges();
    flush([OVERDUE, DUE_TODAY]);
    fixture.detectChanges();

    const rows = fixture.componentInstance.rows();
    expect(rows.length).toBe(2);
    expect(rows[0].customerId).toBe('cust-overdue');
    expect(rows[1].customerId).toBe('cust-today');

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Anil Exports');
    expect(text).toContain('Send revised quote');
    expect(text).toContain('Rahul Mehta');
  });

  it('distinguishes an overdue follow-up from one due today', () => {
    fixture.detectChanges();
    flush([OVERDUE, DUE_TODAY]);
    fixture.detectChanges();

    const rows = fixture.componentInstance.rows();
    const overdueRow = rows.find((r) => r.customerId === 'cust-overdue')!;
    const todayRow = rows.find((r) => r.customerId === 'cust-today')!;

    expect(overdueRow.overdue).toBeTrue();
    expect(overdueRow.dueLabel).toBe('Overdue by 3 days');
    expect(overdueRow.chipBg).toBe('var(--color-danger-bg)');
    expect(overdueRow.chipFg).toBe('var(--color-danger)');

    expect(todayRow.overdue).toBeFalse();
    expect(todayRow.dueLabel).toBe('Due today');
    expect(todayRow.chipBg).toBe('var(--color-warning-bg)');
    expect(todayRow.chipFg).toBe('var(--color-warning)');
  });

  it('shows a friendly empty state instead of a blank table when nothing is due', () => {
    fixture.detectChanges();
    flush([]);
    fixture.detectChanges();

    expect(fixture.componentInstance.empty()).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('Nothing due right now');
  });

  it('shows a visible error instead of hanging when the request fails', () => {
    fixture.detectChanges();
    httpMock
      .expectOne((r) => r.url === '/api/v1/customers/follow-ups/due')
      .flush({ title: 'Server error', detail: 'Lookup failed' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.componentInstance.loading()).toBeFalse();
    expect(fixture.componentInstance.error()).toBe('Lookup failed');
    expect(fixture.nativeElement.textContent).toContain('Lookup failed');
  });

  it('navigates to the right customer when a row is activated (click or Enter)', () => {
    fixture.detectChanges();
    flush([OVERDUE, DUE_TODAY]);
    fixture.detectChanges();

    const navigateSpy = spyOn(router, 'navigate');
    const rowEls: HTMLElement[] = fixture.nativeElement.querySelectorAll('.fu-row');
    expect(rowEls.length).toBe(2);

    rowEls[0].dispatchEvent(new MouseEvent('click'));
    expect(navigateSpy).toHaveBeenCalledWith(['/customers', 'cust-overdue']);

    fixture.componentInstance.openCustomer('cust-today');
    expect(navigateSpy).toHaveBeenCalledWith(['/customers', 'cust-today']);
  });
});
