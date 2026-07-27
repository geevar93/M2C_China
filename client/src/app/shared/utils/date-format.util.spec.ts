import { formatTimelineDate } from './date-format.util';

describe('formatTimelineDate', () => {
  it('formats an ISO 8601 UTC timestamp into "DD Mon YYYY · HH:MM" in local time', () => {
    // Construct the expected string the same way the function does (via
    // local Date getters) so this assertion is stable under any test-runner
    // time zone, rather than hard-coding an IST-assumed clock time.
    const iso = '2026-07-18T14:22:00Z';
    const d = new Date(iso);
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    const expected =
      `${String(d.getDate()).padStart(2, '0')} ${months[d.getMonth()]} ${d.getFullYear()} · ` +
      `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;

    expect(formatTimelineDate(iso)).toBe(expected);
  });

  it('returns an em dash for a null/undefined/unparsable value instead of throwing', () => {
    expect(formatTimelineDate(null)).toBe('—');
    expect(formatTimelineDate(undefined)).toBe('—');
    expect(formatTimelineDate('not-a-date')).toBe('—');
  });
});
