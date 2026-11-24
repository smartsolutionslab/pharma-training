import type { LocationQuery } from 'vue-router';
import { z } from 'zod';

/** The largest page the host takes (GET /certification/me/certificates in openapi.json). */
const lastPage = 21_474_837;

// An invalid page falls back to the first, so it never reaches the host.
const pageSchema = z
  .string()
  .regex(/^[1-9]\d*$/)
  .transform(Number)
  .pipe(z.number().int().max(lastPage))
  .catch(1);

/** The page of the certificates the address names, ?page=n, from 1; the first by default. */
export function parseCertificatesPage(query: LocationQuery): number {
  return pageSchema.parse(query.page);
}
