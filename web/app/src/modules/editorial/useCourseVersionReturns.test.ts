import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, waitFor } from '@testing-library/vue';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { defineComponent, type Ref, ref } from 'vue';
import type { EditorialCourseVersionReturnDto } from '@/api/generated';
import { returnsPageSize, useCourseVersionReturns } from './useCourseVersionReturns';

// The host, as GET /editorial/course-versions/{id}/returns reaches it through the generated client: it answers each
// page with the records the test gives it, newest first.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

const versionId = '0198c1a0-0000-7000-8000-0000000000c1';
const editor = '0198c1a0-0000-7000-8000-0000000000e1';

const newest: EditorialCourseVersionReturnDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a2',
  returnedBy: editor,
  returnedAt: '2026-11-21T14:05:00Z',
  reason: 'Kapitel 2 braucht eine Quelle.',
};
const older: EditorialCourseVersionReturnDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  returnedBy: editor,
  returnedAt: '2026-11-19T08:30:00Z',
  reason: 'Frage 1 ist zu kurz.',
};

/** What the host answers for a page: page 1 holds the newest record, page 2 the older one, of 21 in all. */
let answer: (page: string | null) => Promise<Response>;
const paged = async (page: string | null) =>
  Response.json({ items: page === '2' ? [older] : [newest], totalCount: 21, page: Number(page), pageSize: 20 });

beforeEach(() => {
  answer = paged;
  host.mockImplementation(async (input) => answer(new URL(String(input), 'http://localhost').searchParams.get('page')));
});
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/** Each request the host received, by its path and its query as name=value pairs. */
const asked = () =>
  host.mock.calls.map(([input]) => {
    const url = new URL(String(input), 'http://localhost');
    return { path: url.pathname, query: Object.fromEntries(url.searchParams) };
  });

// A query client of its own per test, so no answer is cached from the test before; it does not retry.
function renderReturns(id: Ref<string>, page: Ref<number>) {
  const used: { returns?: ReturnType<typeof useCourseVersionReturns> } = {};
  const Returns = defineComponent({
    setup() {
      used.returns = useCourseVersionReturns(id, page);
      return () => null;
    },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(Returns, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
  if (used.returns === undefined) {
    throw new Error('The component did not set up.');
  }
  return { ...used.returns, queryClient };
}

test("the history's page size is the host's default", () => {
  expect(returnsPageSize).toBe(20);
});

test('it asks the host for the returns of the version, a page at the default size, and gives the page', async () => {
  const { returns, page, isPending, isError } = renderReturns(ref(versionId), ref(1));

  await waitFor(() => expect(returns.value).toEqual([newest]));
  expect(asked()).toEqual([
    { path: `/api/editorial/course-versions/${versionId}/returns`, query: { page: '1', pageSize: '20' } },
  ]);
  expect(page.value).toEqual({ items: [newest], totalCount: 21, page: 1, pageSize: 20 });
  expect(isPending.value).toBe(false);
  expect(isError.value).toBe(false);
});

test("it asks for the next page when the page changes, and keeps the page before's rows, pending, until it comes", async () => {
  const page = ref(1);
  const { returns, isPending } = renderReturns(ref(versionId), page);
  await waitFor(() => expect(returns.value).toEqual([newest]));
  let arrive: () => void = () => undefined;
  answer = (pageAsked) =>
    new Promise<Response>((resolve) => {
      arrive = () => {
        void paged(pageAsked).then(resolve);
      };
    });

  page.value = 2;

  await waitFor(() =>
    expect(asked().map((request) => request.query)).toEqual([
      { page: '1', pageSize: '20' },
      { page: '2', pageSize: '20' },
    ]),
  );
  expect(returns.value).toEqual([newest]);
  expect(isPending.value).toBe(true);

  arrive();

  await waitFor(() => expect(returns.value).toEqual([older]));
  expect(isPending.value).toBe(false);
});

test('it asks again for the other version when the id changes', async () => {
  const id = ref(versionId);
  const { returns } = renderReturns(id, ref(1));
  await waitFor(() => expect(returns.value).toEqual([newest]));
  const other = '0198c1a0-0000-7000-8000-0000000000c2';

  id.value = other;

  await waitFor(() =>
    expect(asked().map((request) => request.path)).toEqual([
      `/api/editorial/course-versions/${versionId}/returns`,
      `/api/editorial/course-versions/${other}/returns`,
    ]),
  );
});

test('it asks nothing for an id that is no UUID, and stays pending without rows', async () => {
  const { returns, isPending, isError, queryClient } = renderReturns(ref('not-a-uuid'), ref(1));

  // The query is there, and idle: an enabled one would be fetching from the moment its observer subscribed.
  const query = () =>
    queryClient
      .getQueryCache()
      .findAll({ queryKey: ['editorial', 'course-versions', 'not-a-uuid', 'returns'] })
      .at(0);
  await waitFor(() => expect(query()?.state.fetchStatus).toBe('idle'));
  expect(queryClient.isFetching()).toBe(0);
  expect(host).not.toHaveBeenCalled();
  expect(returns.value).toBeUndefined();
  expect(isPending.value).toBe(true);
  expect(isError.value).toBe(false);
});

test('a version the host does not know is an error, not an empty history', async () => {
  answer = async () =>
    Response.json(
      { status: 404, code: 'editorial.course-version-not-found', detail: 'The course version was not found.' },
      { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
    );

  const { returns, isError } = renderReturns(ref(versionId), ref(1));

  await waitFor(() => expect(isError.value).toBe(true));
  expect(returns.value).toBeUndefined();
});
