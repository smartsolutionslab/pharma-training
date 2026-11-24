import { i18n } from '@/i18n';

const calendarDateFormat = /^(\d{4})-(\d{2})-(\d{2})$/;

/** The local day of a date as yyyy-mm-dd. */
function localDay(date: Date): string {
  const year = String(date.getFullYear()).padStart(4, '0');
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/** German dates read day, month, year: 23.11.2026. */
const germanDate = /^(\d{2})\.(\d{2})\.(\d{4})$/;

/** The day the clock shows in Germany, the host's day (DayInGermany()), whatever the browser's zone: yyyy-mm-dd. */
export function today(clock: () => Date): string {
  const shown = i18n.global.d(clock(), 'germanDay', 'de-DE');
  const [, day, month, year] = germanDate.exec(shown) ?? [];
  if (day === undefined || month === undefined || year === undefined) {
    throw new Error(`${shown} is not a German date (dd.mm.yyyy).`);
  }
  return `${year}-${month}-${day}`;
}

/** Whether the value is a day of the calendar written yyyy-mm-dd: 2027-02-30 is not. */
export function isCalendarDate(value: string): boolean {
  const [, year, month, day] = calendarDateFormat.exec(value) ?? [];
  if (year === undefined || month === undefined || day === undefined) {
    return false;
  }
  const date = new Date(Number(year), Number(month) - 1, Number(day));
  return localDay(date) === value;
}
