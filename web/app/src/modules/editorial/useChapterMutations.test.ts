import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render } from '@testing-library/vue';
import { afterEach, expect, test, vi } from 'vitest';
import { defineComponent } from 'vue';
import { type EditorialChapterRequest, getGetEditorialCourseVersionsIdQueryKey } from '@/api/generated';
import { useChapterMutations } from './useChapterMutations';

// Adding, replacing and deleting a chapter of a version, through the generated client. Each answers ok or a failure
// carrying the error the call rejected with, which the page turns into text by its code (#147), and afterwards the version is stale, also
// after a refusal: a 409 (NotADraft, a lost race) means the page shows something that is no longer so.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const versionId = '0198c1a0-0000-7000-8000-0000000000c1';
const chapterId = '0198c1a0-0000-7000-8000-0000000000e1';
const versionKey = getGetEditorialCourseVersionsIdQueryKey(versionId);

const request: EditorialChapterRequest = {
  title: 'Einleitung',
  kind: 'Text',
  text: '# Willkommen',
  file: null,
  durationSeconds: null,
};

// A query client of its own per test, holding the version; it does not retry, so a failure shows at once.
function renderMutations() {
  const used: { mutations?: ReturnType<typeof useChapterMutations> } = {};
  const Mutations = defineComponent({
    setup() {
      used.mutations = useChapterMutations(versionId);
      return () => null;
    },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  queryClient.setQueryData(versionKey, { id: versionId, chapters: [] });
  render(Mutations, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
  if (used.mutations === undefined) {
    throw new Error('The component did not set up.');
  }
  return { mutations: used.mutations, queryClient };
}

const sent = () =>
  host.mock.calls.map(([input, init]) => ({
    url: String(input),
    method: init?.method,
    body: init?.body === undefined ? undefined : (JSON.parse(String(init.body)) as unknown),
  }));

const refusal = (status: number, code: string, detail: string) =>
  new Response(JSON.stringify({ title: 'Conflict', status, detail, code }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });

test('adding posts the chapter to the version and marks the version stale', async () => {
  host.mockImplementation(async () => Response.json(chapterId, { status: 201 }));
  const { mutations, queryClient } = renderMutations();

  await expect(mutations.add(request)).resolves.toEqual({ ok: true });

  expect(sent()).toEqual([
    { url: `/api/editorial/course-versions/${versionId}/chapters`, method: 'POST', body: request },
  ]);
  expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true);
});

test('replacing puts the chapter in its place and marks the version stale', async () => {
  host.mockImplementation(async () => new Response(null, { status: 204 }));
  const { mutations, queryClient } = renderMutations();

  await expect(mutations.replace(chapterId, request)).resolves.toEqual({ ok: true });

  expect(sent()).toEqual([
    { url: `/api/editorial/course-versions/${versionId}/chapters/${chapterId}`, method: 'PUT', body: request },
  ]);
  expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true);
});

test('deleting deletes the chapter and marks the version stale', async () => {
  host.mockImplementation(async () => new Response(null, { status: 204 }));
  const { mutations, queryClient } = renderMutations();

  await expect(mutations.remove(chapterId)).resolves.toEqual({ ok: true });

  expect(sent()).toEqual([
    { url: `/api/editorial/course-versions/${versionId}/chapters/${chapterId}`, method: 'DELETE', body: undefined },
  ]);
  expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true);
});

test.each([
  ['adding', (mutations: ReturnType<typeof useChapterMutations>) => mutations.add(request)],
  ['replacing', (mutations: ReturnType<typeof useChapterMutations>) => mutations.replace(chapterId, request)],
  ['deleting', (mutations: ReturnType<typeof useChapterMutations>) => mutations.remove(chapterId)],
])(
  "%s refused with a module's 409 answers the refusal with its code and marks the version stale",
  async (_change, change) => {
    host.mockImplementation(async () => refusal(409, 'editorial.not-a-draft', 'SERVER WORDS'));
    const { mutations, queryClient } = renderMutations();

    await expect(change(mutations)).resolves.toEqual({
      ok: false,
      error: expect.objectContaining({ status: 409, failureCode: 'editorial.not-a-draft' }),
    });
    expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true);
  },
);

test.each([
  ['adding', (mutations: ReturnType<typeof useChapterMutations>) => mutations.add(request)],
  ['replacing', (mutations: ReturnType<typeof useChapterMutations>) => mutations.replace(chapterId, request)],
  ['deleting', (mutations: ReturnType<typeof useChapterMutations>) => mutations.remove(chapterId)],
])('%s the host cannot do answers a failure with its error, without a code', async (_change, change) => {
  host.mockImplementation(async () => new Response('', { status: 500 }));
  const { mutations } = renderMutations();

  const result = await change(mutations);

  expect(result).toEqual({ ok: false, error: expect.objectContaining({ status: 500, failureCode: undefined }) });
});
