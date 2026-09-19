import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { SearchSelectComponent, SearchSelectOption } from './search-select.component';

const OPTIONS: SearchSelectOption[] = [
  { value: 'a', label: 'Meena Traders', sublabel: 'Meena · +91 98250 41122' },
  { value: 'b', label: 'Sundar Exports' },
  { value: 'c', label: 'Kiran Stores' }
];

describe('SearchSelectComponent', () => {
  let fixture: ComponentFixture<SearchSelectComponent>;
  let component: SearchSelectComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [SearchSelectComponent] }).compileComponents();
    fixture = TestBed.createComponent(SearchSelectComponent);
    component = fixture.componentInstance;
    component.options = OPTIONS;
    component.placeholder = 'Choose a customer';
    el = fixture.nativeElement;
    fixture.detectChanges();
  });

  function trigger(): HTMLButtonElement {
    return el.querySelector('.ss-trigger') as HTMLButtonElement;
  }

  it('shows the placeholder until a value is selected, then the selected label', () => {
    expect(trigger().textContent).toContain('Choose a customer');
    component.value = 'b';
    component.selectedLabel = 'Sundar Exports';
    fixture.detectChanges();
    expect(trigger().textContent).toContain('Sundar Exports');
  });

  it('opens a searchable list and emits the chosen value', fakeAsync(() => {
    const chosen: string[] = [];
    component.valueChange.subscribe((v) => chosen.push(v));

    trigger().click();
    fixture.detectChanges();
    tick();
    const options = el.querySelectorAll('[role="option"]');
    expect(options.length).toBe(3);
    expect(options[0].textContent).toContain('+91 98250 41122');

    (options[2] as HTMLElement).click();
    fixture.detectChanges();
    tick();
    expect(chosen).toEqual(['c']);
    expect(el.querySelector('[role="listbox"]')).toBeNull();
  }));

  it('debounces typing into a single searchChange emission', fakeAsync(() => {
    const terms: string[] = [];
    component.searchChange.subscribe((t) => terms.push(t));
    trigger().click();
    fixture.detectChanges();
    tick();

    const input = el.querySelector('.ss-search') as HTMLInputElement;
    for (const partial of ['s', 'su', 'sun']) {
      input.value = partial;
      input.dispatchEvent(new Event('input'));
      tick(100);
    }
    tick(300);
    expect(terms).toEqual(['sun']);
  }));

  it('supports keyboard selection with arrows and Enter', fakeAsync(() => {
    const chosen: string[] = [];
    component.valueChange.subscribe((v) => chosen.push(v));
    trigger().click();
    fixture.detectChanges();
    tick();

    const input = el.querySelector('.ss-search') as HTMLInputElement;
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    fixture.detectChanges();
    tick();
    expect(chosen).toEqual(['b']);
  }));

  it('shows a no-matches message for an empty result set', fakeAsync(() => {
    trigger().click();
    fixture.detectChanges();
    tick();
    const input = el.querySelector('.ss-search') as HTMLInputElement;
    input.value = 'zzz';
    input.dispatchEvent(new Event('input'));
    component.options = [];
    fixture.detectChanges();
    tick(300);
    expect(el.textContent).toContain('No matches for “zzz”');
  }));
});
