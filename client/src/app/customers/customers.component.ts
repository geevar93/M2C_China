import { Component } from '@angular/core';
import { ComingSoonComponent } from '../shared/components/coming-soon/coming-soon.component';

/** Placeholder — the real customers list ports in M3 (CRM vertical slice) per ACTION_PLAN. */
@Component({
  selector: 'app-customers',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `
    <app-coming-soon
      icon="👥"
      title="Customers"
      description="Search and filter by service type, status and category — ported from the prototype's customers screen in milestone M3 (CRM)."
    ></app-coming-soon>
  `
})
export class CustomersComponent {}
