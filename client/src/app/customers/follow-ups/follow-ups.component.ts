import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../core/services/problem-details.util';
import { TimelineDatePipe } from '../../shared/pipes/timeline-date.pipe';
import { describeFollowUpDue } from '../../shared/utils/date-format.util';
import { CustomersService } from '../services/customers.service';
import { DueFollowUp } from '../models/customer.models';

interface FollowUpRow {
  interactionId: string;
  customerId: string;
  customerName: string;
  text: string;
  authorName: string;
  followUpDate: string;
  overdue: boolean;
  dueLabel: string;
  chipBg: string;
  chipFg: string;
}

/**
 * Due follow-ups (ACTION_PLAN E4-08). Answers FSD Q2: the business asked for
 * an *active reminder* surface, not a passive activity log — this is a
 * standalone worklist of interactions whose `followUpDate` has arrived,
 * earliest due first (the backend already sorts; this component does not
 * re-sort), each row driving straight into the customer it belongs to.
 *
 * No prototype precedent exists for this screen (follow-up reminders post-
 * date the approved prototype) — built from the DESIGN_TOKENS.md atoms only:
 * the shared `.table-wrap` list atom, `.chip` atom, and the existing
 * semantic danger/warning tokens (no new colour introduced) to separate
 * "Overdue" from "Due today".
 */
@Component({
  selector: 'app-follow-ups',
  standalone: true,
  imports: [RouterLink, TimelineDatePipe],
  templateUrl: './follow-ups.component.html',
  styleUrl: './follow-ups.component.scss'
})
export class FollowUpsComponent {
  private readonly customersService = inject(CustomersService);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  private readonly items = signal<DueFollowUp[]>([]);

  readonly rows = computed<FollowUpRow[]>(() => {
    const now = new Date();
    return this.items().map((item) => {
      const due = describeFollowUpDue(item.followUpDate, now);
      return {
        interactionId: item.interactionId,
        customerId: item.customerId,
        customerName: item.customerName,
        text: item.text,
        authorName: item.authorName,
        followUpDate: item.followUpDate,
        overdue: due.overdue,
        dueLabel: due.label,
        chipBg: due.overdue ? 'var(--color-danger-bg)' : 'var(--color-warning-bg)',
        chipFg: due.overdue ? 'var(--color-danger)' : 'var(--color-warning)'
      };
    });
  });

  readonly empty = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

  constructor() {
    this.fetch();
  }

  retry(): void {
    this.fetch();
  }

  openCustomer(customerId: string): void {
    this.router.navigate(['/customers', customerId]);
  }

  private fetch(): void {
    this.loading.set(true);
    this.error.set(null);
    this.customersService.dueFollowUps(new Date().toISOString()).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.error.set(extractErrorMessage(err, 'Could not load due follow-ups. Please try again.'));
      }
    });
  }
}
