import {
  Component,
  DestroyRef,
  ElementRef,
  EventEmitter,
  HostListener,
  Input,
  Output,
  ViewChild,
  inject,
  signal
} from '@angular/core';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

export interface SearchSelectOption {
  value: string;
  label: string;
  /** Secondary line under the label (e.g. contact · phone). */
  sublabel?: string;
}

let nextId = 0;

/**
 * Searchable single-select (combobox). The host owns the option list: it listens
 * to `searchChange` (debounced) and feeds back matching `options`, so large lists
 * are searched server-side rather than rendered in full. `selectedLabel` is passed
 * separately so the trigger keeps showing the current choice even when it is not
 * in the latest result set.
 */
@Component({
  selector: 'app-search-select',
  standalone: true,
  template: `
    <div class="ss" [class.ss--open]="open()">
      <button
        #trigger
        type="button"
        class="field-input ss-trigger"
        role="combobox"
        aria-haspopup="listbox"
        [attr.aria-expanded]="open()"
        [attr.aria-controls]="listId"
        [attr.aria-label]="ariaLabel"
        [disabled]="disabled"
        (click)="toggle()"
        (keydown)="onTriggerKey($event)"
      >
        <span class="ss-trigger__text" [class.ss-trigger__text--placeholder]="!selectedLabel">
          {{ selectedLabel || placeholder }}
        </span>
        <span class="ss-caret" aria-hidden="true">▾</span>
      </button>

      @if (open()) {
        <div class="ss-panel">
          <input
            #searchInput
            class="field-input ss-search"
            type="search"
            [placeholder]="searchPlaceholder"
            [value]="term()"
            [attr.aria-label]="'Search ' + (ariaLabel || '')"
            [attr.aria-controls]="listId"
            [attr.aria-activedescendant]="activeIndex() >= 0 ? listId + '-' + activeIndex() : null"
            (input)="onInput($any($event.target).value)"
            (keydown)="onSearchKey($event)"
          />
          <ul class="ss-list" role="listbox" [id]="listId">
            @if (loading) {
              <li class="ss-state">Searching…</li>
            } @else if (options.length === 0) {
              <li class="ss-state">{{ term() ? 'No matches for “' + term() + '”' : emptyText }}</li>
            } @else {
              @for (opt of options; track opt.value; let i = $index) {
                <li
                  role="option"
                  class="ss-option"
                  [id]="listId + '-' + i"
                  [class.ss-option--active]="i === activeIndex()"
                  [class.ss-option--selected]="opt.value === value"
                  [attr.aria-selected]="opt.value === value"
                  (mousedown)="$event.preventDefault()"
                  (mouseenter)="activeIndex.set(i)"
                  (click)="choose(opt)"
                >
                  <span class="ss-option__label">{{ opt.label }}</span>
                  @if (opt.sublabel) {
                    <span class="ss-option__sub">{{ opt.sublabel }}</span>
                  }
                </li>
              }
            }
          </ul>
          @if (hint) {
            <div class="ss-hint">{{ hint }}</div>
          }
        </div>
      }
    </div>
  `,
  styles: [
    `
      :host { display: block; }
      .ss { position: relative; }
      .ss-trigger {
        display: flex; align-items: center; justify-content: space-between; gap: var(--space-4);
        text-align: left; cursor: pointer;
      }
      .ss-trigger:disabled { cursor: not-allowed; }
      .ss-trigger__text { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .ss-trigger__text--placeholder { color: var(--color-text-placeholder); }
      .ss-caret { color: var(--color-text-muted); font-size: 12px; flex: 0 0 auto; }
      .ss-panel {
        position: absolute; z-index: 60; top: calc(100% + 4px); left: 0; right: 0;
        background: var(--color-surface); border: 1px solid var(--color-border);
        border-radius: var(--radius-md); box-shadow: var(--shadow-dialog); padding: var(--space-4);
      }
      .ss-search { margin-bottom: var(--space-3); }
      .ss-list { list-style: none; margin: 0; padding: 0; max-height: 260px; overflow-y: auto; }
      .ss-option {
        display: flex; flex-direction: column; gap: 1px; padding: 7px var(--space-5);
        border-radius: var(--radius-sm); cursor: pointer;
      }
      .ss-option--active { background: var(--color-surface-hover); }
      .ss-option--selected .ss-option__label { color: var(--color-accent); font-weight: 600; }
      .ss-option__label { font-size: 13.5px; }
      .ss-option__sub { font-size: 12px; color: var(--color-text-muted); }
      .ss-state { padding: var(--space-5); font-size: 13px; color: var(--color-text-muted); }
      .ss-hint { padding: var(--space-3) var(--space-5) 0; font-size: 11.5px; color: var(--color-text-muted); }
    `
  ]
})
export class SearchSelectComponent {
  private readonly host = inject(ElementRef<HTMLElement>);

  @Input() options: SearchSelectOption[] = [];
  @Input() value = '';
  /** Label of the current value, shown on the trigger even when it's not in `options`. */
  @Input() selectedLabel = '';
  @Input() placeholder = 'Select…';
  @Input() searchPlaceholder = 'Type to search…';
  @Input() emptyText = 'Nothing to choose from yet.';
  @Input() hint = '';
  @Input() ariaLabel = '';
  @Input() loading = false;
  @Input() disabled = false;

  @Output() valueChange = new EventEmitter<string>();
  /** Debounced search term; the host re-queries and updates `options`. */
  @Output() searchChange = new EventEmitter<string>();

  @ViewChild('searchInput') private searchInput?: ElementRef<HTMLInputElement>;
  @ViewChild('trigger') private trigger?: ElementRef<HTMLButtonElement>;

  readonly listId = `ss-list-${++nextId}`;
  readonly open = signal(false);
  readonly term = signal('');
  readonly activeIndex = signal(-1);

  private readonly term$ = new Subject<string>();

  constructor() {
    this.term$
      .pipe(debounceTime(250), distinctUntilChanged(), takeUntilDestroyed(inject(DestroyRef)))
      .subscribe((t) => this.searchChange.emit(t));
  }

  toggle(): void {
    if (this.open()) this.close();
    else this.openPanel();
  }

  onInput(value: string): void {
    this.term.set(value);
    this.activeIndex.set(0);
    this.term$.next(value.trim());
  }

  choose(opt: SearchSelectOption): void {
    this.valueChange.emit(opt.value);
    this.close(true);
  }

  onTriggerKey(event: KeyboardEvent): void {
    if (event.key === 'ArrowDown' || event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.openPanel();
    }
  }

  onSearchKey(event: KeyboardEvent): void {
    const count = this.options.length;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (count) this.activeIndex.set((this.activeIndex() + 1) % count);
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (count) this.activeIndex.set((this.activeIndex() - 1 + count) % count);
        break;
      case 'Enter': {
        event.preventDefault();
        const opt = this.options[this.activeIndex()];
        if (opt) this.choose(opt);
        break;
      }
      case 'Escape':
        event.preventDefault();
        this.close(true);
        break;
      case 'Tab':
        this.close();
        break;
    }
  }

  @HostListener('document:mousedown', ['$event'])
  onDocumentMouseDown(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) this.close();
  }

  private openPanel(): void {
    if (this.disabled) return;
    this.open.set(true);
    const selected = this.options.findIndex((o) => o.value === this.value);
    this.activeIndex.set(selected >= 0 ? selected : 0);
    setTimeout(() => this.searchInput?.nativeElement.focus());
  }

  private close(refocus = false): void {
    this.open.set(false);
    if (this.term()) {
      // Reset the search so reopening starts from the default list.
      this.term.set('');
      this.term$.next('');
    }
    if (refocus) setTimeout(() => this.trigger?.nativeElement.focus());
  }
}
