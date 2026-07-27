import { describeFollowUpDue, formatTimelineDate } from './date-format.util';

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

describe('describeFollowUpDue', () => {
  const now = new Date(2026, 6, 27, 15, 0, 0); // 27 Jul 2026, 15:00 local

  it('reports "Due today" for a due date earlier the same local calendar day', () => {
    const dueEarlierToday = new Date(2026, 6, 27, 9, 30, 0).toISOString();
    const result = describeFollowUpDue(dueEarlierToday, now);
    expect(result.overdue).toBeFalse();
    expect(result.label).toBe('Due today');
  });

  it('reports "Overdue by 1 day" for a due date exactly one calendar day back', () => {
    const dueYesterday = new Date(2026, 6, 26, 9, 0, 0).toISOString();
    const result = describeFollowUpDue(dueYesterday, now);
    expect(result.overdue).toBeTrue();
    expect(result.label).toBe('Overdue by 1 day');
  });

  it('reports "Overdue by N days" for a due date several calendar days back', () => {
    const dueThreeDaysAgo = new Date(2026, 6, 24, 9, 0, 0).toISOString();
    const result = describeFollowUpDue(dueThreeDaysAgo, now);
    expect(result.overdue).toBeTrue();
    expect(result.label).toBe('Overdue by 3 days');
  });

  it('returns an em dash for a null/undefined/unparsable value instead of throwing', () => {
    expect(describeFollowUpDue(null, now)).toEqual({ overdue: false, label: '—' });
    expect(describeFollowUpDue(undefined, now)).toEqual({ overdue: false, label: '—' });
    expect(describeFollowUpDue('not-a-date', now)).toEqual({ overdue: false, label: '—' });
  });
});
