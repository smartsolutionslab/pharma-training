import { afterEach, describe, expect, test } from 'vitest';
import { i18n, type Language, setLanguage } from '@/i18n';
import { calendarDate } from './formats';

// Dates and numbers are formatted per language through vue-i18n's d() and n(), never by hand.
const { d, n } = i18n.global;
const noBreakSpace = ' ';
const timeZone = process.env.TZ;

afterEach(() => {
  // Node applies a change of TZ at once; the test process gets its own zone back.
  if (timeZone === undefined) {
    delete process.env.TZ;
  } else {
    process.env.TZ = timeZone;
  }
});

const formats: [Language, string, string, string, string][] = [
  ['de-DE', '22.11.2026', `80${noBreakSpace}%`, '1.312', '01.01.2026'],
  ['en-GB', '22/11/2026', '80%', '1,312', '01/01/2026'],
];

describe.each(formats)('in %s', (language, date, percent, integer, newYear) => {
  test('a date shows day, month and year', () => {
    setLanguage(language, { remember: false });

    expect(d(new Date(2026, 10, 22), 'date')).toBe(date);
  });

  test('a date and time shows the date with hour and minute', () => {
    setLanguage(language, { remember: false });

    const shown = d(new Date(2026, 10, 22, 14, 5), 'dateTime');

    expect(shown).toContain(date);
    expect(shown).toContain('14:05');
  });

  test('a share is a percentage', () => {
    setLanguage(language, { remember: false });

    expect(n(0.8, 'percent')).toBe(percent);
  });

  test('a count groups its thousands', () => {
    setLanguage(language, { remember: false });

    expect(n(1312, 'integer')).toBe(integer);
  });

  test.each(['America/New_York', 'Pacific/Honolulu'])(
    'a calendar date stays on its day in the time zone %s',
    (zone) => {
      setLanguage(language, { remember: false });
      process.env.TZ = zone;
      // The zone took effect: read as an instant at UTC midnight, the same text is the day before here.
      expect(new Date('2026-01-01').getDate()).toBe(31);

      expect(d(calendarDate('2026-01-01'), 'date')).toBe(newYear);
      expect(d(calendarDate('2026-11-22'), 'date')).toBe(date);
    },
  );
});

test('a calendar date is that day at local midnight', () => {
  const day = calendarDate('2026-11-22');

  expect([day.getFullYear(), day.getMonth(), day.getDate(), day.getHours(), day.getMinutes()]).toEqual([
    2026, 10, 22, 0, 0,
  ]);
});
