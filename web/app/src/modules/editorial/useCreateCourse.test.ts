import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render } from '@testing-library/vue';
import { afterEach, expect, test, vi } from 'vitest';
import { defineComponent } from 'vue';
import { getGetEditorialCourseVersionsQueryKey } from '@/api/generated';
import { useCreateCourse } from './useCreateCourse';

// The host, as POST /editorial/courses reaches it through the generated client.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const started = '0198c1a0-0000-7000-8000-0000000000c9';

/** Inbox pages the cache holds, as the page asked for them: the drafts and the versions in review. */
const inboxKeys = [
  getGetEditorialCourseVersionsQueryKey({ status: 'Draft', page: 1, pageSize: 20 }),
  getGetEditorialCourseVersionsQueryKey({ status: 'InReview', page: 2, pageSize: 20 }),
];

// A query client of its own per test, holding the inbox's pages; it does not retry, so a failure shows at once.
function renderCreation() {
  const used: { creation?: ReturnType<typeof useCreateCourse> } = {};
  const Creation = defineComponent({
    setup() {
      used.creation = useCreateCourse();
      return () => null;
    },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  for (const key of inboxKeys) {
    queryClient.setQueryData(key, { items: [], totalCount: 0, page: 1, pageSize: 20 });
  }
  render(Creation, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
  if (used.creation === undefined) {
    throw new Error('The component did not set up.');
  }
  return { creation: used.creation, queryClient };
}

const posted = () =>
  host.mock.calls.map(([input, init]) => ({
    url: String(input),
    method: init?.method,
    body: JSON.parse(String(init?.body)) as unknown,
  }));

test('a created course answers its first version id and marks every inbox page stale', async () => {
  host.mockImplementation(async () => Response.json(started, { status: 201 }));
  const { creation, queryClient } = renderCreation();

  const result = await creation.create('Hygiene II');

  expect(result).toEqual({ ok: true, id: started });
  expect(posted()).toEqual([{ url: '/api/editorial/courses', method: 'POST', body: { title: 'Hygiene II' } }]);
  for (const key of inboxKeys) {
    expect(queryClient.getQueryState(key)?.isInvalidated).toBe(true);
  }
});

test("a course the host refuses with a module's code answers a failure carrying the refusal with its code", async () => {
  host.mockImplementation(
    async () =>
      new Response(
        JSON.stringify({
          title: 'Bad Request',
          status: 400,
          detail: 'SERVER WORDS',
          code: 'editorial.invalid-course-title',
        }),
        { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
      ),
  );
  const { creation } = renderCreation();

  await expect(creation.create('Hygiene II')).resolves.toEqual({
    ok: false,
    error: expect.objectContaining({ status: 400, failureCode: 'editorial.invalid-course-title' }),
  });
});

test('a course the host cannot create answers a failure carrying its error, without a code', async () => {
  host.mockImplementation(async () => new Response('', { status: 500 }));
  const { creation } = renderCreation();

  const result = await creation.create('Hygiene II');

  expect(result).toEqual({ ok: false, error: expect.objectContaining({ status: 500, failureCode: undefined }) });
});
