import { Pipe, PipeTransform, inject } from '@angular/core';
import { StatusStyleService } from '../services/status-style.service';

/**
 * `{{ status.code | statusBg }}` / `{{ status.code | statusFg }}` — reads the
 * shared StatusStyleService so no component ever redefines the ST/SVC maps.
 */
@Pipe({ name: 'statusBg', standalone: true, pure: true })
export class StatusBgPipe implements PipeTransform {
  private readonly styles = inject(StatusStyleService);

  transform(code: string | null | undefined): string {
    return this.styles.status(code).bg;
  }
}

@Pipe({ name: 'statusFg', standalone: true, pure: true })
export class StatusFgPipe implements PipeTransform {
  private readonly styles = inject(StatusStyleService);

  transform(code: string | null | undefined): string {
    return this.styles.status(code).fg;
  }
}

@Pipe({ name: 'svcBg', standalone: true, pure: true })
export class ServiceTypeBgPipe implements PipeTransform {
  private readonly styles = inject(StatusStyleService);

  transform(code: string | null | undefined): string {
    return this.styles.serviceType(code).bg;
  }
}

@Pipe({ name: 'svcFg', standalone: true, pure: true })
export class ServiceTypeFgPipe implements PipeTransform {
  private readonly styles = inject(StatusStyleService);

  transform(code: string | null | undefined): string {
    return this.styles.serviceType(code).fg;
  }
}

@Pipe({ name: 'svcLabel', standalone: true, pure: true })
export class ServiceTypeLabelPipe implements PipeTransform {
  private readonly styles = inject(StatusStyleService);

  transform(code: string | null | undefined): string {
    return this.styles.serviceType(code).label;
  }
}
