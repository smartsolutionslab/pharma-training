import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render } from '@testing-library/vue';
import { afterEach, describe, expect, test, vi } from 'vitest';
import { defineComponent } from 'vue';
import {
  getGetEditorialCourseVersionsIdQueryKey,
  getGetEditorialCourseVersionsIdReturnsQueryKey,
  getGetEditorialCourseVersionsQueryKey,
} from '@/api/generated';
import { ApiError } from '@/api/baseApi';
import { useCourseVersionWorkflow } from './useCourseVersionWorkflow';

// The workflow's four steps (#26) as the course version page takes them: POST /editorial/course-versions/{id}/submit,
// /publish, /return with { reason } and /retire, through the generated client. Each answers { ok: true } or
// { ok: false, error } with what the client threw. A step taken changes the version, its returns and the inbox: every
// cached entry below ['editorial', 'course-versions'] is stale. A refused one may mean the version changed meanwhile:
// the version is stale, nothing else.

const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const versionId = '0198c1a0-0000-7000-8000-0000000000c1';
const otherVersionId = '0198c1a0-0000-7000-8000-0000000000c9';

const versionKey = getGetEditorialCourseVersionsIdQueryKey(versionId);
const returnsKey = getGetEditorialCourseVersionsIdReturnsQueryKey(versionId, { page: 1, pageSize: 20 });
const inboxKeys = [
  getGetEditorialCourseVersionsQueryKey({ status: 'Draft', page: 1, pageSize: 20 }),
  getGetEditorialCourseVersionsQueryKey({ status: 'InReview', page: 1, pageSize: 20 }),
  getGetEditorialCourseVersionsQueryKey({ status: 'Published', page: 2, pageSize: 20 }),
];
/** A cached query of another module: a step of the workflow leaves it alone. */
const unrelatedKey = ['catalog', 'courses'];

type Workflow = ReturnType<typeof useCourseVersionWorkflow>;

// A query client of its own per test, holding the version, its returns, inbox pages and another module's entry; it
// does not retry, so a failure shows at once.
function renderWorkflow() {
  const used: { workflow?: Workflow } = {};
  const Workflow = defineComponent({
    setup() {
      used.workflow = useCourseVersionWorkflow(versionId);
      return () => null;
    },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  queryClient.setQueryData(versionKey, { id: versionId });
  queryClient.setQueryData(returnsKey, { items: [], totalCount: 0, page: 1, pageSize: 20 });
  for (const key of inboxKeys) {
    queryClient.setQueryData(key, { items: [], totalCount: 0, page: 1, pageSize: 20 });
  }
  queryClient.setQueryData(unrelatedKey, []);
  render(Workflow, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
  if (used.workflow === undefined) {
    throw new Error('The component did not set up.');
  }
  return { workflow: used.workflow, queryClient };
}

const sent = () =>
  host.mock.calls.map(([input, init]) => ({
    url: String(input),
    method: init?.method,
    body: init?.body === undefined ? undefined : JSON.parse(String(init.body)),
  }));

const refusal = (status: number, code: string) =>
  new Response(JSON.stringify({ title: 'Refused', status, code }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });

const stale = (queryClient: QueryClient, key: readonly unknown[]) => queryClient.getQueryState(key)?.isInvalidated;

/** Each step, how the page takes it, and what it sends. */
const steps: [string, (workflow: Workflow) => Promise<unknown>, string, unknown][] = [
  ['submit', (workflow) => workflow.submit(), 'submit', undefined],
  ['publish', (workflow) => workflow.publish(), 'publish', undefined],
  [
    'return',
    (workflow) => workflow.returnToDraft('Kapitel 2 fehlt die Quelle.'),
    'return',
    { reason: 'Kapitel 2 fehlt die Quelle.' },
  ],
  ['retire', (workflow) => workflow.retire(), 'retire', undefined],
];

describe.each(steps)('%s', (_name, take, path, body) => {
  test('posts to its path, answers ok and marks the version, its returns and every inbox page stale', async () => {
    host.mockImplementation(async () => Response.json({}));
    const { workflow, queryClient } = renderWorkflow();

    const result = await take(workflow);

    expect(result).toEqual({ ok: true });
    expect(sent()).toEqual([{ url: `/api/editorial/course-versions/${versionId}/${path}`, method: 'POST', body }]);
    expect(stale(queryClient, versionKey)).toBe(true);
    expect(stale(queryClient, returnsKey)).toBe(true);
    for (const key of inboxKeys) {
      expect(stale(queryClient, key)).toBe(true);
    }
    expect(stale(queryClient, unrelatedKey)).toBe(false);
  });

  // A conflict may mean another tab returned the version meanwhile (#376): its returns are read again with it.
  test('refused for a conflict, answers the error and marks the version and its returns stale, not the inbox', async () => {
    host.mockImplementation(async () => refusal(409, 'editorial.course-version-changed'));
    const { workflow, queryClient } = renderWorkflow();

    const result = await take(workflow);

    expect(result).toEqual({ ok: false, error: expect.any(ApiError) });
    const { error } = result as { error: ApiError };
    expect(error.status).toBe(409);
    expect(error.code).toBe('editorial.course-version-changed');
    expect(stale(queryClient, versionKey)).toBe(true);
    expect(stale(queryClient, returnsKey)).toBe(true);
    for (const key of inboxKeys) {
      expect(stale(queryClient, key)).toBe(false);
    }
  });

  test('failing on the network, answers the TypeError and marks only the version stale', async () => {
    host.mockImplementation(() => Promise.reject(new TypeError('Failed to fetch')));
    const { workflow, queryClient } = renderWorkflow();

    const result = await take(workflow);

    expect(result).toEqual({ ok: false, error: expect.any(TypeError) });
    expect(stale(queryClient, versionKey)).toBe(true);
    expect(stale(queryClient, returnsKey)).toBe(false);
  });

  test('is pending while the host has not answered, and no longer once it has', async () => {
    let answer: (response: Response) => void = () => undefined;
    host.mockImplementation(
      () =>
        new Promise<Response>((resolve) => {
          answer = resolve;
        }),
    );
    const { workflow } = renderWorkflow();
    expect(workflow.isPending.value).toBe(false);

    const result = take(workflow);

    await vi.waitFor(() => expect(workflow.isPending.value).toBe(true));
    answer(Response.json({}));
    await expect(result).resolves.toEqual({ ok: true });
    await vi.waitFor(() => expect(workflow.isPending.value).toBe(false));
  });
});

test("a step leaves another version's entry alone only when it was refused", async () => {
  host.mockImplementation(async () => refusal(409, 'editorial.not-in-review'));
  const { workflow, queryClient } = renderWorkflow();
  const otherKey = getGetEditorialCourseVersionsIdQueryKey(otherVersionId);
  queryClient.setQueryData(otherKey, { id: otherVersionId });

  await workflow.publish();

  expect(stale(queryClient, otherKey)).toBe(false);
});

test('a return sends the reason it is given, as it is', async () => {
  host.mockImplementation(async () => Response.json({}));
  const { workflow } = renderWorkflow();

  await workflow.returnToDraft('Zeile 1\nZeile 2 <b>fett</b>');

  expect(sent()[0]?.body).toEqual({ reason: 'Zeile 1\nZeile 2 <b>fett</b>' });
});
