import { execFileSync } from 'node:child_process';

// The one way the e2e database helpers (e2e/certificationDatabase.ts, e2e/complianceDatabase.ts) reach the PostgreSQL
// the AppHost runs in Docker: with docker exec, signing in with the container's own credentials, so no password is in
// a test. Aspire names the container after its resource, postgres-<suffix>, and gives it no label naming the resource;
// its orchestrator (DCP) labels every container it starts with com.microsoft.developer.usvc-dev.name, so a container
// counts only with both that label and the name: another postgres-* container on the machine, which Aspire did not
// start, is never picked.

/** The label Aspire's orchestrator puts on every container it starts, holding the container's name. */
const aspireContainerLabel = 'com.microsoft.developer.usvc-dev.name';

/** The one running PostgreSQL container of the AppHost; fails, saying why, when there is none or more than one. */
function postgresContainer(database: string) {
  let names: string[];
  try {
    names = execFileSync(
      'docker',
      ['ps', '--filter', `label=${aspireContainerLabel}`, '--filter', 'name=^postgres-', '--format', '{{.Names}}'],
      { encoding: 'utf8' },
    )
      .split('\n')
      .map((name) => name.trim())
      .filter((name) => name !== '');
  } catch (error) {
    throw new Error(`Reaching the ${database} database needs Docker, and docker ps failed: ${String(error)}`, {
      cause: error,
    });
  }
  if (names.length !== 1) {
    throw new Error(
      `Reaching the ${database} database needs exactly one running container named postgres-* that Aspire started ` +
        `(labelled ${aspireContainerLabel}), as the AppHost starts it; found ` +
        `${names.length === 0 ? 'none' : names.join(', ')}. Is the AppHost running?`,
    );
  }
  return names[0] ?? '';
}

/** Runs SQL against the module's database and returns psql's unaligned, tuples-only answer. */
export function sqlIn(database: 'certification' | 'compliance', sql: string) {
  // The name goes into the container's shell command: only the modules' own names, whatever the type says.
  if (!['certification', 'compliance'].includes(database)) {
    throw new Error(`No e2e helper reads the database ${String(database)}.`);
  }
  return execFileSync(
    'docker',
    [
      'exec',
      '-i',
      postgresContainer(database),
      'sh',
      '-c',
      `PGPASSWORD="$POSTGRES_PASSWORD" psql -U "\${POSTGRES_USER:-postgres}" -d ${database} -v ON_ERROR_STOP=1 -qAt`,
    ],
    { input: sql, encoding: 'utf8' },
  ).trim();
}
