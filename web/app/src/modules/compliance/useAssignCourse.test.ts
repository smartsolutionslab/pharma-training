import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, waitFor } from '@testing-library/vue';
import { afterEach, describe, expect, test, vi } from 'vitest';
import { defineComponent } from 'vue';
import {
  type AssignComplianceCourseRequest,
  getGetCatalogCoursesQueryKey,
  getGetCompliancePharmaciesIdMatrixQueryKey,
} from '@/api/generated';
import { useAssignCourse } from './useAssignCourse';

// Assigning a course (#37, T4): POST /compliance/pharmacies/{id}/assignments, and what its answer means for the wizard.
// A 200 carries who was assigned and who was refused; a refusal of the whole request is sorted by what the user can do
// about it: fix the due date, choose another course, nothing here at all, look again, or try again.

const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/** Problem details as the host sends a module's refusal (or the framework's, for already-exists). */
function problem(status: number, code: string, detail: string): Response {
  return new Response(JSON.stringify({ title: 'Refused', status, detail, code }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}

const pharmacy = '0198c1a0-0000-7000-8000-0000000000a1';
const request: AssignComplianceCourseRequest = {
  courseId: '0198c1a0-0000-7000-8000-0000000000c1',
  personIds: ['0198c1a0-0000-7000-8000-0000000000e1', '0198c1a0-0000-7000-8000-0000000000e2'],
  dueDate: '2027-03-15',
  recurrenceMonths: 12,
};

/** Catalog pages the cache holds, as the course step asked for them. */
const catalogKeys = [
  getGetCatalogCoursesQueryKey({ page: 1, pageSize: 20 }),
  getGetCatalogCoursesQueryKey({ page: 2, pageSize: 20 }),
];

const otherPharmacy = '0198c1a0-0000-7000-8000-0000000000a2';

/** Matrix pages the cache holds (#36), as the team matrix asked for them: this pharmacy's, filtered or not, and another's. */
const matrixKeys = [
  getGetCompliancePharmaciesIdMatrixQueryKey(pharmacy, { page: 1, pageSize: 50 }),
  getGetCompliancePharmaciesIdMatrixQueryKey(pharmacy, { page: 2, pageSize: 50, jobFunction: 'Pta' }),
];
const otherMatrixKey = getGetCompliancePharmaciesIdMatrixQueryKey(otherPharmacy, { page: 1, pageSize: 50 });
const emptyMatrix = { courses: [], members: { items: [], totalCount: 0, page: 1, pageSize: 50 } };

function renderAssignment() {
  const used: { assignment?: ReturnType<typeof useAssignCourse> } = {};
  const Assignment = defineComponent({
    setup() {
      used.assignment = useAssignCourse();
      return () => null;
    },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  for (const key of catalogKeys) {
    queryClient.setQueryData(key, { items: [], totalCount: 0, page: 1, pageSize: 20 });
  }
  for (const key of [...matrixKeys, otherMatrixKey]) {
    queryClient.setQueryData(key, emptyMatrix);
  }
  render(Assignment, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
  if (used.assignment === undefined) {
    throw new Error('The component did not set up.');
  }
  return { assignment: used.assignment, queryClient };
}

test('an assignment posts the request to the pharmacy and answers who was assigned and who was refused', async () => {
  const answer = {
    assigned: [{ personId: request.personIds?.[0], assignmentId: '0198c1a0-0000-7000-8000-0000000000f1' }],
    refused: [{ personId: request.personIds?.[1], code: 'compliance.assignment-already-active', message: 'aktiv' }],
  };
  host.mockImplementation(async () => Response.json(answer));
  const { assignment } = renderAssignment();

  const outcome = await assignment.assign(pharmacy, request);

  expect(outcome).toEqual({ kind: 'done', assigned: answer.assigned, refused: answer.refused });
  expect(
    host.mock.calls.map(([input, init]) => ({
      url: String(input),
      method: init?.method,
      body: JSON.parse(String(init?.body)) as unknown,
    })),
  ).toEqual([{ url: `/api/compliance/pharmacies/${pharmacy}/assignments`, method: 'POST', body: request }]);
});

test('an assignment is under way until the host answers', async () => {
  let answer: (response: Response) => void = () => undefined;
  host.mockImplementation(() => new Promise<Response>((resolve) => (answer = resolve)));
  const { assignment } = renderAssignment();
  expect(assignment.isAssigning.value).toBe(false);

  const outcome = assignment.assign(pharmacy, request);

  await waitFor(() => expect(assignment.isAssigning.value).toBe(true));
  answer(Response.json({ assigned: [], refused: [] }));
  await outcome;
  await waitFor(() => expect(assignment.isAssigning.value).toBe(false));
});

// The team matrix (#36) shows the new cells on the way back: an assignment that assigned someone marks every cached
// matrix page of that pharmacy stale, by the matrix's key without its parameters. One that assigned nobody changed no
// cell, and leaves the matrix as it is; another pharmacy's matrix is never touched.
test('an assignment that assigned someone marks the pharmacy’s matrix stale, and one that assigned nobody does not', async () => {
  const learner = request.personIds?.[0] ?? '';
  host.mockImplementationOnce(async () =>
    Response.json({
      assigned: [],
      refused: [{ personId: learner, code: 'compliance.assignment-already-active', message: 'aktiv' }],
    }),
  );
  host.mockImplementationOnce(async () =>
    Response.json({
      assigned: [{ personId: learner, assignmentId: '0198c1a0-0000-7000-8000-0000000000f1' }],
      refused: [],
    }),
  );
  const { assignment, queryClient } = renderAssignment();

  await expect(assignment.assign(pharmacy, request)).resolves.toMatchObject({ kind: 'done', assigned: [] });
  for (const key of matrixKeys) {
    expect(queryClient.getQueryState(key)?.isInvalidated).toBe(false);
  }

  await expect(assignment.assign(pharmacy, request)).resolves.toMatchObject({ kind: 'done' });
  for (const key of matrixKeys) {
    expect(queryClient.getQueryState(key)?.isInvalidated).toBe(true);
  }
  expect(queryClient.getQueryState(otherMatrixKey)?.isInvalidated).toBe(false);
  for (const key of catalogKeys) {
    expect(queryClient.getQueryState(key)?.isInvalidated).toBe(false);
  }
});

describe('a refusal of the whole request', () => {
  test.each([
    [422, 'compliance.due-date-in-the-past'],
    [400, 'compliance.due-date-too-far'],
    [400, 'compliance.due-date-missing'],
  ])('%i %s is about the due date, by its code', async (status, code) => {
    host.mockImplementation(async () => problem(status, code, 'Das Datum.'));
    const { assignment } = renderAssignment();

    await expect(assignment.assign(pharmacy, request)).resolves.toEqual({ kind: 'dueDate', code });
  });

  test.each([
    [409, 'compliance.course-retired'],
    [404, 'compliance.course-not-found'],
  ])('%i %s means the course is gone, and marks every catalog page stale', async (status, code) => {
    host.mockImplementation(async () => problem(status, code, 'Die Schulung.'));
    const { assignment, queryClient } = renderAssignment();

    await expect(assignment.assign(pharmacy, request)).resolves.toEqual({ kind: 'courseGone' });
    for (const key of catalogKeys) {
      expect(queryClient.getQueryState(key)?.isInvalidated).toBe(true);
    }
  });

  // The page's own words for capability-missing, the catalog's for pharmacy-not-found (#147), never the host's.
  test.each([
    [403, 'compliance.capability-missing', 'compliance.assign.notAllowed'],
    [404, 'compliance.pharmacy-not-found', 'errors.compliance.pharmacy-not-found'],
  ])('%i %s means the user cannot assign here at all, in the words of %s', async (status, code, key) => {
    host.mockImplementation(async () => problem(status, code, 'SERVER WORDS'));
    const { assignment } = renderAssignment();

    await expect(assignment.assign(pharmacy, request)).resolves.toEqual({
      kind: 'notAvailable',
      message: { key, values: expect.any(Object) },
    });
  });

  test.each([
    [409, 'compliance.assignments-changed'],
    [409, 'already-exists'],
  ])('%i %s means the assignments changed meanwhile', async (status, code) => {
    host.mockImplementation(async () => problem(status, code, 'Geändert.'));
    const { assignment, queryClient } = renderAssignment();

    await expect(assignment.assign(pharmacy, request)).resolves.toEqual({ kind: 'changed' });
    for (const key of catalogKeys) {
      expect(queryClient.getQueryState(key)?.isInvalidated).toBe(false);
    }
  });

  test.each([
    ['a failure of the host', () => new Response('', { status: 500 })],
    ['a request too large', () => new Response('', { status: 413 })],
    ['no token', () => new Response('', { status: 401 })],
    ['an incomplete identity', () => problem(403, 'compliance.identity-incomplete', 'Identität unvollständig.')],
    ['too many learners', () => problem(400, 'compliance.too-many-learners', 'Zu viele.')],
    ['an invalid recurrence', () => problem(400, 'compliance.invalid-recurrence', 'Ungültig.')],
  ])('%s is a failure', async (_name, refusal) => {
    host.mockImplementation(async () => refusal());
    const { assignment } = renderAssignment();

    await expect(assignment.assign(pharmacy, request)).resolves.toMatchObject({ kind: 'failed' });
  });
});
