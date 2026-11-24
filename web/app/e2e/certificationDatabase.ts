import { execFileSync } from 'node:child_process';
import { randomBytes, randomUUID } from 'node:crypto';

// A test helper that reads and writes Certification's database directly, for what no API offers: the verification
// code is in no answer the holder reads (it is only in the PDF's QR link), so the e2e takes it from the row; a
// certificate is valid for months, so an expired one can only be made by moving its stored validUntil; and, while #46
// has not merged, nothing short of the database issues a certificate at all. It reaches the PostgreSQL the AppHost runs
// in Docker (a container named postgres-<suffix>, here and in CI) with docker exec, signing in with the container's own
// credentials, so no password is in the test. Every value is validated before it goes into SQL.
//
// seedCertificate is the fallback while #46 is unmerged (#54, T9): it inserts a row as the CertificateIssued consumer
// would, with document_state Pending and no document. #50's reaction renders the PDF on issuing, not on an insert, so a
// seeded certificate covers verification and the list, and its PDF only as "being created".

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const day = /^\d{4}-\d{2}-\d{2}$/;
const instant = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$/;
const base64Url = /^[A-Za-z0-9_-]{24,64}$/;

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
    throw new Error(`Reaching the certification database needs Docker, and docker ps failed: ${String(error)}`, {
      cause: error,
    });
  }
  if (names.length !== 1) {
    throw new Error(
      `Reaching the certification database needs exactly one running container named postgres-*, as the AppHost ` +
        `starts it; found ${names.length === 0 ? 'none' : names.join(', ')}. Is the AppHost running?`,
    );
  }
  return names[0] ?? '';
}

/** Runs SQL against the certification database and returns psql's unaligned, tuples-only answer. */
function certificationSql(sql: string) {
  return execFileSync(
    'docker',
    [
      'exec',
      '-i',
      postgresContainer(),
      'sh',
      '-c',
      'PGPASSWORD="$POSTGRES_PASSWORD" psql -U "${POSTGRES_USER:-postgres}" -d certification -v ON_ERROR_STOP=1 -qAt',
    ],
    { input: sql, encoding: 'utf8' },
  ).trim();
}

/** A text as a SQL string literal: quotes doubled, and refused when it holds a control character or is too long. */
function literal(text: string, maxLength: number) {
  if (text.trim() === '' || text.length > maxLength || /\p{Cc}/u.test(text)) {
    throw new Error(`A seeded text must be non-blank, at most ${maxLength} characters, without control characters.`);
  }
  return `'${text.replaceAll("'", "''")}'`;
}

/** A fresh verification code as the host draws one: 24 random bytes, 32 Base64Url characters. */
export function newVerificationCode() {
  return randomBytes(24).toString('base64url');
}

export type SeededCertificate = {
  /** The holder's person id; any Guid, since verification reads only the stored name. */
  holderId: string;
  /** As the PDF and the verification page show it, at most 100 characters. */
  holderName: string;
  /** At most 150 characters. */
  courseTitle: string;
  /** The instant of issuing, in UTC (yyyy-mm-ddThh:mm:ssZ); the page shows its day in Germany. */
  issuedAt: string;
  /** The last valid day (yyyy-mm-dd). */
  validUntil: string;
};

/**
 * Inserts a certificate into certification.certificates with a fresh id, attempt, course, course version and
 * verification code, document Pending and no location; returns its id.
 */
export function seedCertificate(certificate: SeededCertificate) {
  const { holderId, holderName, courseTitle, issuedAt, validUntil } = certificate;
  if (!uuid.test(holderId) || !instant.test(issuedAt) || !day.test(validUntil)) {
    throw new Error(
      `seedCertificate needs a holder id, a UTC instant and a yyyy-mm-dd day, not ${holderId}, ${issuedAt} and ` +
        `${validUntil}.`,
    );
  }
  const id = randomUUID();
  const sql =
    'INSERT INTO certification.certificates (id, attempt_id, holder_id, holder_name, course_id, course_version_id, ' +
    'course_title, score_percent, issued_at, valid_until, verification_code, document_state, document_location) ' +
    `VALUES ('${id}', '${randomUUID()}', '${holderId}', ${literal(holderName, 100)}, '${randomUUID()}', ` +
    `'${randomUUID()}', ${literal(courseTitle, 150)}, 100, TIMESTAMPTZ '${issuedAt}', DATE '${validUntil}', ` +
    `'${newVerificationCode()}', 'Pending', NULL) RETURNING id;\n`;
  const inserted = certificationSql(sql);
  if (inserted !== id) {
    throw new Error(`Seeding certificate ${id} failed; psql answered: ${inserted}`);
  }
  return id;
}

/** The certificate's verification code, from certification.certificates; fails unless exactly that row has one. */
export function verificationCodeOf(certificateId: string) {
  if (!uuid.test(certificateId)) {
    throw new Error(`verificationCodeOf needs a certificate id, not ${certificateId}.`);
  }
  const code = certificationSql(
    `SELECT verification_code FROM certification.certificates WHERE id = '${certificateId}';\n`,
  );
  if (!base64Url.test(code)) {
    throw new Error(`The certification database has no certificate ${certificateId}; psql answered: ${code}`);
  }
  return code;
}

/** Moves the certificate's last valid day (yyyy-mm-dd); fails unless exactly that row changed. */
export function setValidUntil(certificateId: string, validUntil: string) {
  if (!uuid.test(certificateId) || !day.test(validUntil)) {
    throw new Error(
      `setValidUntil needs a certificate id and a yyyy-mm-dd day, not ${certificateId} and ${validUntil}.`,
    );
  }
  const updated = certificationSql(
    `UPDATE certification.certificates SET valid_until = DATE '${validUntil}' WHERE id = '${certificateId}' RETURNING id;\n`,
  );
  if (updated !== certificateId) {
    throw new Error(`The certification database has no certificate ${certificateId}; psql answered: ${updated}`);
  }
}

/** A certificate Certification issued, by its id and its verification code. */
export type IssuedCertificate = { id: string; verificationCode: string };

/**
 * The one certificate Certification issued to the holder (by the name it stores) for the course (by its title), from
 * certification.certificates; none while it has not been issued. No API the holder reads lists their certificates yet
 * (GET /certification/me/certificates, Z6), so the end-to-end flow (#55) finds the one it earned here.
 */
export function issuedCertificate(holderName: string, courseTitle: string): IssuedCertificate | undefined {
  const rows = certificationSql(
    `SELECT id || '|' || verification_code FROM certification.certificates WHERE holder_name = ${literal(holderName, 100)} ` +
      `AND course_title = ${literal(courseTitle, 150)};\n`,
  )
    .split('\n')
    .filter((row) => row !== '');
  if (rows.length > 1) {
    throw new Error(`Certification issued ${rows.length} certificates to ${holderName} for ${courseTitle}, not one.`);
  }
  const [id = '', verificationCode = ''] = (rows[0] ?? '').split('|');
  if (rows.length === 0) {
    return undefined;
  }
  if (!uuid.test(id) || !base64Url.test(verificationCode)) {
    throw new Error(`The certification database answered an unexpected row: ${rows[0] ?? ''}`);
  }
  return { id, verificationCode };
}
