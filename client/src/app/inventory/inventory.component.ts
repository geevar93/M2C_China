import { Component } from '@angular/core';
import { ComingSoonComponent } from '../shared/components/coming-soon/coming-soon.component';

/** Placeholder — the real inventory list ports in M5 (Inventory & Shipments) per ACTION_PLAN. */
@Component({
  selector: 'app-inventory',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `
    <app-coming-soon
      icon="📦"
      title="Inventory"
      description="Search, filter and the low-stock visual bar — ported from the prototype's inventory screen in milestone M5 (Inventory & Shipments)."
    ></app-coming-soon>
  `
})
export class InventoryComponent {}
