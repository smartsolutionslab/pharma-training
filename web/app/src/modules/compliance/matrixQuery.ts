import type { LocationQuery } from 'vue-router';
import { z } from 'zod';
import { ComplianceJobFunction } from '@/api/generated';

/** The largest page the host takes (GET /compliance/pharmacies/{id}/matrix in openapi.json). */
const lastPage = 21_474_837;

// Each part of the address falls back on its own, so an invalid value never reaches the host: no course, no job
// function, the first page.
const matrixQuerySchema = z.object({
  course: z.uuid().optional().catch(undefined),
  jobFunction: z.enum(Object.values(ComplianceJobFunction)).optional().catch(undefined),
  page: z
    .string()
    .regex(/^[1-9]\d*$/)
    .transform(Number)
    .pipe(z.number().int().max(lastPage))
    .catch(1),
});

/** What the team matrix shows: a course of its own choosing, the host's job-function filter and a page, from 1. */
export type MatrixQuery = z.infer<typeof matrixQuerySchema>;

/** The matrix's state from its address, ?course=<uuid>&jobFunction=<name>&page=n; the first page, unfiltered, by default. */
export function parseMatrixQuery(query: LocationQuery): MatrixQuery {
  const { course, jobFunction, page } = matrixQuerySchema.parse({
    course: query.course,
    jobFunction: query.jobFunction,
    page: query.page,
  });
  return {
    ...(course === undefined ? {} : { course }),
    ...(jobFunction === undefined ? {} : { jobFunction }),
    page,
  };
}

/** The address of a matrix's state, without the first page and without a filter that is not set. */
export function toLocationQuery(query: MatrixQuery): LocationQuery {
  const address: LocationQuery = {};
  if (query.course !== undefined) {
    address.course = query.course;
  }
  if (query.jobFunction !== undefined) {
    address.jobFunction = query.jobFunction;
  }
  if (query.page !== 1) {
    address.page = String(query.page);
  }
  return address;
}
