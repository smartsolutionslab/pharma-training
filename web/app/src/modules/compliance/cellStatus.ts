import type { ComplianceAssignmentState } from '@/api/generated';
import { calendarDate } from '@/i18n/formats';

/** What a cell of the team matrix shows: the assignment's state, or Expired, which the host does not name. */
export type CellStatus = ComplianceAssignmentState | 'Expired';

/** The part of a matrix cell its status depends on. */
export interface StatusOfCell {
  state: ComplianceAssignmentState;
  /** The last valid day (yyyy-mm-dd), inclusive, of a completion; null without one. */
  validUntil: string | null;
}

/**
 * A cell's status on a day: its state, except that a completion (Completed or CompletedLate) is Expired from local
 * midnight after its validUntil, the last day it holds.
 */
export function cellStatus(cell: StatusOfCell, today: Date): CellStatus {
  const completed = cell.state === 'Completed' || cell.state === 'CompletedLate';
  if (!completed || cell.validUntil === null) {
    return cell.state;
  }
  const startOfToday = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  return calendarDate(cell.validUntil) < startOfToday ? 'Expired' : cell.state;
}
