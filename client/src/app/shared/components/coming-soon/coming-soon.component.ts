import { Component, Input } from '@angular/core';

/**
 * Placeholder for feature screens that are out of scope this pass (M3+ per
 * ACTION_PLAN — the prototype's real screens are ported in later milestones).
 * Kept as one shared component so every stub route looks consistent rather
 * than six near-identical inline templates.
 */
@Component({
  selector: 'app-coming-soon',
  standalone: true,
  template: `
    <div class="card state-panel" style="padding: 64px 24px;">
      <div style="font-size: 32px;">{{ icon }}</div>
      <h1 style="font-size: 20px; font-weight: 700; margin: 0; color: var(--color-text);">{{ title }}</h1>
      <p style="max-width: 420px;">{{ description }}</p>
    </div>
  `
})
export class ComingSoonComponent {
  @Input() icon = '🚧';
  @Input() title = 'Coming soon';
  @Input() description = 'This screen is planned for a later milestone.';
}
