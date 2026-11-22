import type { Language } from './language';

// Dates and numbers are formatted through vue-i18n's d() and n() with these formats, in the browser's time zone.
const date: Intl.DateTimeFormatOptions = { day: '2-digit', month: '2-digit', year: 'numeric' };
const dateTime: Intl.DateTimeFormatOptions = { ...date, hour: '2-digit', minute: '2-digit' };

const integer: Intl.NumberFormatOptions = { maximumFractionDigits: 0 };
const percent: Intl.NumberFormatOptions = { style: 'percent', maximumFractionDigits: 0 };

/** d(value, 'date') gives 22.11.2026 and 22/11/2026; 'dateTime' adds hour and minute. */
export const datetimeFormats = {
  'de-DE': { date, dateTime },
  'en-GB': { date, dateTime },
} satisfies Record<Language, Record<string, Intl.DateTimeFormatOptions>>;

/** n(value, 'integer') gives 1.312 and 1,312; n(share, 'percent') gives 80 % and 80%. */
export const numberFormats = {
  'de-DE': { integer, percent },
  'en-GB': { integer, percent },
} satisfies Record<Language, Record<string, Intl.NumberFormatOptions>>;

const calendarDateFormat = /^(\d{4})-(\d{2})-(\d{2})$/;

/**
 * A calendar date (yyyy-mm-dd) as that day at local midnight. new Date('2026-01-01') would be midnight UTC, which is
 * the day before in a time zone west of it.
 */
export function calendarDate(value: string): Date {
  const [, year, month, day] = calendarDateFormat.exec(value) ?? [];
  if (year === undefined || month === undefined || day === undefined) {
    throw new Error(`${value} is not a calendar date (yyyy-mm-dd).`);
  }
  return new Date(Number(year), Number(month) - 1, Number(day));
}
