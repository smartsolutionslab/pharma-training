import type { LocationQuery } from 'vue-router';
import { z } from 'zod';

/** The statuses the inbox shows, in the order of its tabs; Superseded and Retired versions are not in it. */
export const inboxStatuses = ['Draft', 'InReview', 'Published'] as const;

/** The largest page the host takes (GET /editorial/course-versions in openapi.json). */
const lastPage = 21_474_837;

// Each part of the address falls back to its default on its own, so an invalid value never reaches the host.
const inboxQuerySchema = z.object({
  status: z.enum(inboxStatuses).catch('Draft'),
  page: z
    .string()
    .regex(/^[1-9]\d*$/)
    .transform(Number)
    .pipe(z.number().int().max(lastPage))
    .catch(1),
});

/** What the inbox shows: one of its statuses and a page of it, from 1. */
export type InboxQuery = z.infer<typeof inboxQuerySchema>;

/** The inbox's state from its address, ?status=Draft|InReview|Published&page=n; the first page of the drafts by default. */
export function parseInboxQuery(query: LocationQuery): InboxQuery {
  return inboxQuerySchema.parse({ status: query.status, page: query.page });
}
