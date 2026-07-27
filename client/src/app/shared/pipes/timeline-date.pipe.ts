import { Pipe, PipeTransform } from '@angular/core';
import { formatTimelineDate } from '../utils/date-format.util';

/** `{{ event.occurredAtUtc | timelineDate }}` — see date-format.util.ts for the format rationale. */
@Pipe({ name: 'timelineDate', standalone: true, pure: true })
export class TimelineDatePipe implements PipeTransform {
  transform(isoUtc: string | null | undefined): string {
    return formatTimelineDate(isoUtc);
  }
}
