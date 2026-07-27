import { Component } from '@angular/core';
import { ComingSoonComponent } from '../shared/components/coming-soon/coming-soon.component';

/** Placeholder — the real shipments list ports in M5 (Inventory & Shipments) per ACTION_PLAN. */
@Component({
  selector: 'app-shipments',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `
    <app-coming-soon
      icon="🚚"
      title="Shipments"
      description="Status tabs over outbound shipments — ported from the prototype's shipments screen in milestone M5 (Inventory & Shipments)."
    ></app-coming-soon>
  `
})
export class ShipmentsComponent {}
