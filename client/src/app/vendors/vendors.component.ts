import { Component } from '@angular/core';
import { ComingSoonComponent } from '../shared/components/coming-soon/coming-soon.component';

/** Placeholder — the real vendors list ports in M4 (Sourcing) per ACTION_PLAN. */
@Component({
  selector: 'app-vendors',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `
    <app-coming-soon
      icon="🏭"
      title="Vendors"
      description="Search and filter by category, region and status — ported from the prototype's vendors screen in milestone M4 (Sourcing)."
    ></app-coming-soon>
  `
})
export class VendorsComponent {}
