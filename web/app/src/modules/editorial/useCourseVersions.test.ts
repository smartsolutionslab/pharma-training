import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, waitFor } from '@testing-library/vue';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { defineComponent, type Ref, ref } from 'vue';
import type { EditorialCourseVersionSummaryDto } from '@/api/generated';
import type { InboxQuery } from './inboxQuery';
import { useCourseVersions } from './useCourseVersions';

// The host, as GET /editorial/course-versions reaches it through the generated client: it answers each status with the
// versions the test gives it.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

const hygiene: EditorialCourseVersionSummaryDto = {
  id: '0198c1a0-0000-7000-8000-0000000000c1',
  courseId: '0198c1a0-0000-7000-8000-0000000000d1',
  number: 2,
  title: 'Hygiene in der Apotheke',
  status: 'Draft',
};
const labelling: EditorialCourseVersionSummaryDto = {
  id: '0198c1a0-0000-7000-8000-0000000000c2',
  courseId: '0198c1a0-0000-7000-8000-0000000000d2',
  number: 1,
  title: 'Kennzeichnung von Rezepturen',
  status: 'InReview',
};

beforeEach(() => {
  host.mockImplementation(async (input) => {
    const status = new URL(String(input), 'http://localhost').searchParams.get('status');
    const items = [hygiene, labelling].filter((version) => version.status === status);
    return Response.json({ items, totalCount: items.length, page: 1, pageSize: 20 });
  });
});
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/** The query string of each request the host received, as name=value pairs. */
const asked = () =>
  host.mock.calls.map(([input]) => {
    const url = new URL(String(input), 'http://localhost');
    return { path: url.pathname, query: Object.fromEntries(url.searchParams) };
  });

// A query client of its own per test, so no answer is cached from the test before; it does not retry.
function renderVersions(query: Ref<InboxQuery>) {
  const used: { versions?: ReturnType<typeof useCourseVersions> } = {};
  const Versions = defineComponent({
    setup() {
      used.versions = useCourseVersions(query);
      return () => null;
    },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(Versions, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
  if (used.versions === undefined) {
    throw new Error('The component did not set up.');
  }
  return used.versions;
}

test('it asks the host for the versions of the status and the page, at the default page size', async () => {
  const { versions, page } = renderVersions(ref<InboxQuery>({ status: 'InReview', page: 2 }));

  await waitFor(() => expect(versions.value?.map((version) => version.title)).toEqual([labelling.title]));
  expect(asked()).toEqual([
    { path: '/api/editorial/course-versions', query: { status: 'InReview', page: '2', pageSize: '20' } },
  ]);
  expect(page.value).toMatchObject({ totalCount: 1, page: 1, pageSize: 20 });
});

test('it asks again when the status or the page changes', async () => {
  const query = ref<InboxQuery>({ status: 'Draft', page: 1 });
  const { versions } = renderVersions(query);
  await waitFor(() => expect(versions.value?.map((version) => version.title)).toEqual([hygiene.title]));

  query.value = { status: 'InReview', page: 1 };

  await waitFor(() => expect(versions.value?.map((version) => version.title)).toEqual([labelling.title]));
  query.value = { status: 'InReview', page: 3 };
  await waitFor(() =>
    expect(asked().map((request) => request.query)).toEqual([
      { status: 'Draft', page: '1', pageSize: '20' },
      { status: 'InReview', page: '1', pageSize: '20' },
      { status: 'InReview', page: '3', pageSize: '20' },
    ]),
  );
});

test('a list the host cannot give is an error, not an empty list', async () => {
  host.mockImplementation(async () => new Response('', { status: 500 }));

  const { versions, isError } = renderVersions(ref<InboxQuery>({ status: 'Draft', page: 1 }));

  await waitFor(() => expect(isError.value).toBe(true));
  expect(versions.value).toBeUndefined();
});
