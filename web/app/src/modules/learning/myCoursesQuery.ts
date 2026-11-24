import type { LocationQuery } from 'vue-router';
import { z } from 'zod';

/** The largest page the host takes (GET /compliance/me/assignments in openapi.json). */
const lastPage = 21_474_837;

// An invalid page never reaches the host: it is the first page.
const myCoursesQuerySchema = z.object({
  page: z
    .string()
    .regex(/^[1-9]\d*$/)
    .transform(Number)
    .pipe(z.number().int().max(lastPage))
    .catch(1),
});

/** What my courses shows: a page, from 1. */
export type MyCoursesQuery = z.infer<typeof myCoursesQuerySchema>;

/** My courses' state from its address, ?page=n; the first page by default. Nothing else in the address is read. */
export function parseMyCoursesQuery(query: LocationQuery): MyCoursesQuery {
  return myCoursesQuerySchema.parse({ page: query.page });
}

/** The address of my courses' state, without the first page. */
export function toLocationQuery(query: MyCoursesQuery): LocationQuery {
  const address: LocationQuery = {};
  if (query.page !== 1) {
    address.page = String(query.page);
  }
  return address;
}
