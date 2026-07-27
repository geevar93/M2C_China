import { Component } from '@angular/core';
import { ComingSoonComponent } from '../shared/components/coming-soon/coming-soon.component';

/** Placeholder — the real dashboard (stat tiles, funnel, charts) ports in M7 per ACTION_PLAN. */
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `
    <app-coming-soon
      icon="📊"
      title="Operations Dashboard"
      description="Stat tiles, lead funnel, category mix, service split and shipment status — ported from the prototype's dash screen in milestone M7 (Analytics)."
    ></app-coming-soon>
  `
})
export class DashboardComponent {}
