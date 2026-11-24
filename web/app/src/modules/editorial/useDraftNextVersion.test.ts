import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render } from '@testing-library/vue';
import { afterEach, expect, test, vi } from 'vitest';
import { defineComponent } from 'vue';
import { getGetEditorialCourseVersionsIdQueryKey, getGetEditorialCourseVersionsQueryKey } from '@/api/generated';
import { useDraftNextVersion } from './useDraftNextVersion';

// The host, as POST /editorial/course-versions/{id}/next reaches it through the generated client.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const published = '0198c1a0-0000-7000-8000-0000000000c1';
const drafted = '0198c1a0-0000-7000-8000-0000000000c2';

/** Inbox pages the cache holds, as the inbox asked for them: the drafts and the published versions. */
const inboxKeys = [
  getGetEditorialCourseVersionsQueryKey({ status: 'Draft', page: 1, pageSize: 20 }),
  getGetEditorialCourseVersionsQueryKey({ status: 'Published', page: 2, pageSize: 20 }),
];
const sourceKey = getGetEditorialCourseVersionsIdQueryKey(published);

// A query client of its own per test, holding the inbox's pages and the published version; it does not retry, so a
// failure shows at once.
function renderDrafting() {
  const used: { drafting?: ReturnType<typeof useDraftNextVersion> } = {};
  const Drafting = defineComponent({
    setup() {
      used.drafting = useDraftNextVersion(published);
      return () => null;
    },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  for (const key of inboxKeys) {
    queryClient.setQueryData(key, { items: [], totalCount: 0, page: 1, pageSize: 20 });
  }
  queryClient.setQueryData(sourceKey, { id: published });
  render(Drafting, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
  if (used.drafting === undefined) {
    throw new Error('The component did not set up.');
  }
  return { drafting: used.drafting, queryClient };
}

const sent = () =>
  host.mock.calls.map(([input, init]) => ({ url: String(input), method: init?.method, body: init?.body }));

const refusal = (status: number, code: string, title: string) =>
  new Response(JSON.stringify({ title, status, code }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });

test('a drafted next version answers its id, posts nothing but the path, and marks every inbox page stale', async () => {
  host.mockImplementation(async () =>
    Response.json(drafted, { status: 201, headers: { Location: `/editorial/course-versions/${drafted}` } }),
  );
  const { drafting, queryClient } = renderDrafting();

  const result = await drafting.draftNext();

  expect(result).toEqual({ ok: true, id: drafted });
  expect(sent()).toEqual([
    { url: `/api/editorial/course-versions/${published}/next`, method: 'POST', body: undefined },
  ]);
  for (const key of inboxKeys) {
    expect(queryClient.getQueryState(key)?.isInvalidated).toBe(true);
  }
});

test.each([
  [409, 'editorial.next-version-exists', 'Version 3 of this course already exists.'],
  [409, 'editorial.not-published', 'Die Version ist nicht veröffentlicht.'],
  [409, 'editorial.course-version-changed', 'The course version changed in the meantime.'],
  [404, 'editorial.course-version-not-found', 'There is no course version.'],
  [403, 'editorial.identity-incomplete', 'The access token names no subject that identifies a person.'],
])(
  'a next version the host refuses with %i %s answers a failure carrying the refusal with its code, and the version is stale',
  async (status, code, title) => {
    host.mockImplementation(async () => refusal(status, code, title));
    const { drafting, queryClient } = renderDrafting();

    const result = await drafting.draftNext();

    expect(result).toEqual({ ok: false, error: expect.objectContaining({ status, failureCode: code }) });
    expect(queryClient.getQueryState(sourceKey)?.isInvalidated).toBe(true);
  },
);

test.each([
  ['a 500', () => Promise.resolve(new Response('', { status: 500 }))],
  ['a policy 403 without a code', () => Promise.resolve(refusal(403, '', 'Forbidden'))],
  ["the unique index's 409 already-exists", () => Promise.resolve(refusal(409, 'already-exists', 'Conflict'))],
  ['a network failure', () => Promise.reject(new TypeError('Failed to fetch'))],
])('%s answers a failure carrying the error it rejected with', async (_, answer) => {
  host.mockImplementation(answer);
  const { drafting } = renderDrafting();

  const result = await drafting.draftNext();

  expect(result).toEqual({ ok: false, error: expect.any(Error) });
});

test('is drafting while the host has not answered, and no longer once it has', async () => {
  let answer: (response: Response) => void = () => undefined;
  host.mockImplementation(
    () =>
      new Promise<Response>((resolve) => {
        answer = resolve;
      }),
  );
  const { drafting } = renderDrafting();
  expect(drafting.isDrafting.value).toBe(false);

  const result = drafting.draftNext();

  await vi.waitFor(() => expect(drafting.isDrafting.value).toBe(true));
  answer(Response.json(drafted, { status: 201 }));
  await expect(result).resolves.toEqual({ ok: true, id: drafted });
  await vi.waitFor(() => expect(drafting.isDrafting.value).toBe(false));
});
