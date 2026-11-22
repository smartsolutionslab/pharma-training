import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render } from '@testing-library/vue';
import { afterEach, expect, test, vi } from 'vitest';
import { defineComponent } from 'vue';
import { type EditorialQuestionRequest, getGetEditorialCourseVersionsIdQueryKey } from '@/api/generated';
import { introduction, refusal, versionId } from './courseTestFixtures';
import { useQuestionMutations } from './useQuestionMutations';

// Adding, replacing and deleting a question of a version, through the generated client (#25, S5). Each answers ok or
// a failure with the host's words where a module sent some (ApiError.serverMessage), and afterwards the version is
// stale, also after a refusal: a 409 (NotADraft) means the page shows something that is no longer so.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);
afterEach(() => host.mockReset());
afterEach(cleanup);

const questionId = '0198c1a0-0000-7000-8000-0000000000a1';
const versionKey = getGetEditorialCourseVersionsIdQueryKey(versionId);

const request: EditorialQuestionRequest = {
  chapterId: introduction.id,
  text: 'Wie lange waschen Sie die Hände?',
  type: 'SingleChoice',
  options: [
    { text: '30 Sekunden', correct: true },
    { text: '5 Sekunden', correct: false },
  ],
};

type Mutations = ReturnType<typeof useQuestionMutations>;

// A query client of its own per test, holding the version; it does not retry, so a failure shows at once.
function renderMutations() {
  const used: { mutations?: Mutations } = {};
  const Host = defineComponent({
    setup() {
      used.mutations = useQuestionMutations(versionId);
      return () => null;
    },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  queryClient.setQueryData(versionKey, { id: versionId, questions: [] });
  render(Host, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
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

const questionsUrl = `/api/editorial/course-versions/${versionId}/questions`;

test('adding posts the question to the version and marks the version stale', async () => {
  host.mockImplementation(async () => Response.json(questionId, { status: 201 }));
  const { mutations, queryClient } = renderMutations();

  await expect(mutations.add(request)).resolves.toEqual({ ok: true });

  expect(sent()).toEqual([{ url: questionsUrl, method: 'POST', body: request }]);
  expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true);
});

test('replacing puts the question in its place and marks the version stale', async () => {
  host.mockImplementation(async () => Response.json({ value: null }));
  const { mutations, queryClient } = renderMutations();

  await expect(mutations.replace(questionId, request)).resolves.toEqual({ ok: true });

  expect(sent()).toEqual([{ url: `${questionsUrl}/${questionId}`, method: 'PUT', body: request }]);
  expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true);
});

test('deleting deletes the question and marks the version stale', async () => {
  host.mockImplementation(async () => Response.json({ value: null }));
  const { mutations, queryClient } = renderMutations();

  await expect(mutations.remove(questionId)).resolves.toEqual({ ok: true });

  expect(sent()).toEqual([{ url: `${questionsUrl}/${questionId}`, method: 'DELETE', body: undefined }]);
  expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true);
});

const changes: [string, (mutations: Mutations) => Promise<unknown>][] = [
  ['adding', (mutations) => mutations.add(request)],
  ['replacing', (mutations) => mutations.replace(questionId, request)],
  ['deleting', (mutations) => mutations.remove(questionId)],
];

test.each(changes)(
  "%s refused with a module's 409 answers the host's words; every change reloads the version, also after a refusal",
  async (_change, change) => {
    host.mockImplementation(async () =>
      refusal(409, 'editorial.not-a-draft', 'Die Version ist kein Entwurf mehr und kann nicht geändert werden.'),
    );
    const { mutations, queryClient } = renderMutations();

    await expect(change(mutations)).resolves.toEqual({
      ok: false,
      serverMessage: 'Die Version ist kein Entwurf mehr und kann nicht geändert werden.',
    });
    expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true);
  },
);

test.each(changes)('%s the host cannot do answers a failure without words of the host', async (_change, change) => {
  host.mockImplementation(async () => new Response('', { status: 500 }));
  const { mutations, queryClient } = renderMutations();

  await expect(change(mutations)).resolves.toEqual({ ok: false });
  expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true);
});

test.each(changes)('%s that never reached the host answers a failure', async (_change, change) => {
  host.mockImplementation(async () => {
    throw new TypeError('Failed to fetch');
  });
  const { mutations, queryClient } = renderMutations();

  await expect(change(mutations)).resolves.toEqual({ ok: false });
  expect(queryClient.getQueryState(versionKey)?.isInvalidated).toBe(true);
});
