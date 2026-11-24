import { execFileSync } from 'node:child_process';

// A test helper that writes to Compliance's database directly, for what the API refuses on purpose: assigning refuses a
// due date in the past (422 compliance.due-date-in-the-past), and nothing else moves one there, so an overdue course
// can only be made by moving a stored due date. It reaches the PostgreSQL the AppHost runs in Docker (a container named
// postgres-<suffix>, here and in CI) with docker exec, signing in with the container's own credentials, so no password
// is in the test. Compliance reads "overdue" from the due date on every read (Assignment.StateOn), so the due date is
// all there is to change.

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const day = /^\d{4}-\d{2}-\d{2}$/;

/** The one running PostgreSQL container of the AppHost; fails, saying why, when there is none or more than one. */
function postgresContainer() {
  let names: string[];
  try {
    names = execFileSync('docker', ['ps', '--filter', 'name=^postgres-', '--format', '{{.Names}}'], {
      encoding: 'utf8',
    })
      .split('\n')
      .map((name) => name.trim())
      .filter((name) => name !== '');
  } catch (error) {
    throw new Error(`Seeding the compliance database needs Docker, and docker ps failed: ${String(error)}`, {
      cause: error,
    });
  }
  if (names.length !== 1) {
    throw new Error(
      `Seeding the compliance database needs exactly one running container named postgres-*, as the AppHost starts ` +
        `it; found ${names.length === 0 ? 'none' : names.join(', ')}. Is the AppHost running?`,
    );
  }
  return names[0] ?? '';
}

/** Moves the assignment's due date (yyyy-mm-dd) in compliance.assignments; fails unless exactly that row changed. */
export function setDueDate(assignmentId: string, dueDate: string) {
  if (!uuid.test(assignmentId) || !day.test(dueDate)) {
    throw new Error(`setDueDate needs an assignment id and a yyyy-mm-dd day, not ${assignmentId} and ${dueDate}.`);
  }
  const sql = `UPDATE compliance.assignments SET due_date = DATE '${dueDate}' WHERE id = '${assignmentId}' RETURNING id;\n`;
  const updated = execFileSync(
    'docker',
    [
      'exec',
      '-i',
      postgresContainer(),
      'sh',
      '-c',
      'PGPASSWORD="$POSTGRES_PASSWORD" psql -U "${POSTGRES_USER:-postgres}" -d compliance -v ON_ERROR_STOP=1 -qAt',
    ],
    { input: sql, encoding: 'utf8' },
  ).trim();
  if (updated !== assignmentId) {
    throw new Error(`The compliance database has no assignment ${assignmentId}; psql answered: ${updated}`);
  }
}
