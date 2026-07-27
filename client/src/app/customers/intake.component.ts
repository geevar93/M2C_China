import { Component } from '@angular/core';
import { ComingSoonComponent } from '../shared/components/coming-soon/coming-soon.component';

/** Placeholder — the real intake form ports in M3 (CRM vertical slice) per ACTION_PLAN. */
@Component({
  selector: 'app-customer-intake',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `
    <app-coming-soon
      icon="📝"
      title="New Lead Intake"
      description="Log a WhatsApp or phone enquiry — ported from the prototype's intake screen in milestone M3 (CRM)."
    ></app-coming-soon>
  `
})
export class CustomerIntakeComponent {}
