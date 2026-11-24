import { sqlIn } from './postgres';

// A test helper that writes to Compliance's database directly, for what the API refuses on purpose: assigning refuses a
// due date in the past (422 compliance.due-date-in-the-past), and nothing else moves one there, so an overdue course
// can only be made by moving a stored due date. It reaches the PostgreSQL the AppHost runs in Docker through
// e2e/postgres.ts, so no password is in the test. Compliance reads "overdue" from the due date on every read
// (Assignment.StateOn), so the due date is all there is to change.

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const day = /^\d{4}-\d{2}-\d{2}$/;

/** Moves the assignment's due date (yyyy-mm-dd) in compliance.assignments; fails unless exactly that row changed. */
export function setDueDate(assignmentId: string, dueDate: string) {
  if (!uuid.test(assignmentId) || !day.test(dueDate)) {
    throw new Error(`setDueDate needs an assignment id and a yyyy-mm-dd day, not ${assignmentId} and ${dueDate}.`);
  }
  const sql = `UPDATE compliance.assignments SET due_date = DATE '${dueDate}' WHERE id = '${assignmentId}' RETURNING id;\n`;
  const updated = sqlIn('compliance', sql);
  if (updated !== assignmentId) {
    throw new Error(`The compliance database has no assignment ${assignmentId}; psql answered: ${updated}`);
  }
}
