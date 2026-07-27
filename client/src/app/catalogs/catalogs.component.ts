import { Component } from '@angular/core';
import { ComingSoonComponent } from '../shared/components/coming-soon/coming-soon.component';

/** Placeholder — the real catalog browse/upload ports in M4 (Sourcing) per ACTION_PLAN. */
@Component({
  selector: 'app-catalogs',
  standalone: true,
  imports: [ComingSoonComponent],
  template: `
    <app-coming-soon
      icon="📚"
      title="Catalogs"
      description="Cross-vendor browse by category, vendor and title, plus the upload dialog — ported from the prototype's catalogs screen in milestone M4 (Sourcing)."
    ></app-coming-soon>
  `
})
export class CatalogsComponent {}
