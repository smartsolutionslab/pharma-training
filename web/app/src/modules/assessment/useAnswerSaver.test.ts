import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, waitFor } from '@testing-library/vue';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { defineComponent } from 'vue';
import { type AssessmentExaminationDto, getGetAssessmentExaminationsBySessionSessionIdQueryKey } from '@/api/generated';
import {
  coat,
  deferred,
  durationQuestion,
  examinationId,
  fiveSeconds,
  gloves,
  inAttempt,
  protectionQuestion,
  refusal,
  saved,
  sessionId,
  thirtySeconds,
} from './examinationFixtures';
import { type Fetch, requestsOf } from './examinationHost';
import { useAnswerSaver } from './useAnswerSaver';

// useAnswerSaver(sessionId, examinationId) saves the learner's answers (#48) with
// PUT /assessment/examinations/{id}/attempts/current/answers/{questionId} { optionIds } through the generated client:
// - choose(questionId, optionIds) keeps the choice as the question's draft and saves it at once;
// - one request at a time per examination, since concurrent answers would race into examination-changed; while one
//   is on its way, only each question's latest draft waits, so repeated clicks send once with the latest choice;
// - a 409 examination-changed is sent again by itself, up to 3 times (ADR 0016: sending again is safe);
// - on 204, if the draft has not changed since it was sent, chosenOptionIds go into the by-session query's cache
//   (setQueryData, no invalidation) and the draft is dropped;
// - an empty set is never sent (the backend refuses it, an answer cannot be cleared): the state says keepOne;
// - a 409 no-open-attempt stops it: closed, the read invalidated, nothing sent any more;
// - stateOf(questionId) is { kind: 'idle' | 'saving' | 'saved' | 'keepOne' } or { kind: 'failed', failure }, the
//   failure as assessmentErrors.ts says it; retry(questionId) saves the draft again; draftOf(questionId) is the draft;
// - settled() resolves once nothing waits or is on its way: true when every answer is saved, false when one failed.
const host = vi.fn<Fetch>();
vi.stubGlobal('fetch', host);

let answering: (questionId: string, optionIds: string[]) => Promise<Response>;
beforeEach(() => {
  answering = async () => saved();
  host.mockImplementation(async (input, init) => {
    const questionId = String(input).split('/').at(-1) ?? '';
    const { optionIds } = JSON.parse(String(init?.body)) as { optionIds: string[] };
    return answering(questionId, optionIds);
  });
});
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const examinationKey = getGetAssessmentExaminationsBySessionSessionIdQueryKey(sessionId);

/** The saver in a component of its own, with a query client holding the examination as it was read. */
function renderSaver(read: AssessmentExaminationDto = inAttempt()) {
  const used: { saver?: ReturnType<typeof useAnswerSaver> } = {};
  const Saver = defineComponent({
    setup() {
      used.saver = useAnswerSaver(sessionId, examinationId);
      return () => null;
    },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  queryClient.setQueryData(examinationKey, read);
  render(Saver, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });
  if (used.saver === undefined) {
    throw new Error('The component did not set up.');
  }
  return { saver: used.saver, queryClient };
}

const sent = () => requestsOf(host.mock.calls);
const answerUrl = (questionId: string) =>
  `/assessment/examinations/${examinationId}/attempts/current/answers/${questionId}`;

/** The options the cached examination holds as chosen for the question. */
const cachedChoice = (queryClient: QueryClient, questionId: string) =>
  queryClient
    .getQueryData<AssessmentExaminationDto>(examinationKey)
    ?.currentAttempt?.questions.find((question) => question.id === questionId)?.chosenOptionIds;

test('a choice is put at once to the current attempt through the generated client', async () => {
  const { saver } = renderSaver();

  saver.choose(durationQuestion.id, [thirtySeconds]);

  await waitFor(() =>
    expect(sent()).toEqual([
      { method: 'PUT', path: answerUrl(durationQuestion.id), body: { optionIds: [thirtySeconds] } },
    ]),
  );
});

test('one request at a time: a second question waits until the first is answered', async () => {
  const first = deferred();
  const log: string[] = [];
  answering = (questionId) => {
    log.push(`put ${questionId}`);
    return questionId === durationQuestion.id ? first.answer : Promise.resolve(saved());
  };
  const { saver } = renderSaver();

  saver.choose(durationQuestion.id, [thirtySeconds]);
  await waitFor(() => expect(sent()).toHaveLength(1));
  saver.choose(protectionQuestion.id, [coat]);
  expect(saver.stateOf(protectionQuestion.id).kind).toBe('saving');

  log.push('first answered');
  first.release(saved());

  await waitFor(() => expect(sent()).toHaveLength(2));
  // Nothing more went while the first was on its way.
  expect(log).toEqual([`put ${durationQuestion.id}`, 'first answered', `put ${protectionQuestion.id}`]);
  expect(sent()[1]).toEqual({ method: 'PUT', path: answerUrl(protectionQuestion.id), body: { optionIds: [coat] } });
});

test('repeated choices while one is on its way are sent once, with the latest choice', async () => {
  const first = deferred();
  let calls = 0;
  answering = () => {
    calls += 1;
    return calls === 1 ? first.answer : Promise.resolve(saved());
  };
  const { saver, queryClient } = renderSaver();

  saver.choose(durationQuestion.id, [fiveSeconds]);
  await waitFor(() => expect(sent()).toHaveLength(1));
  saver.choose(durationQuestion.id, [thirtySeconds]);
  saver.choose(durationQuestion.id, [fiveSeconds]);
  saver.choose(durationQuestion.id, [thirtySeconds]);
  first.release(saved());

  await waitFor(() => expect(saver.stateOf(durationQuestion.id).kind).toBe('saved'));
  expect(sent().map(({ body }) => body)).toEqual([{ optionIds: [fiveSeconds] }, { optionIds: [thirtySeconds] }]);
  expect(cachedChoice(queryClient, durationQuestion.id)).toEqual([thirtySeconds]);
});

test('a draft changed while its save was on its way is kept, and the cache never takes the older choice', async () => {
  const first = deferred();
  const second = deferred();
  const answers = [first.answer, second.answer];
  answering = () => answers.shift() ?? Promise.resolve(saved());
  const { saver, queryClient } = renderSaver();

  saver.choose(durationQuestion.id, [fiveSeconds]);
  await waitFor(() => expect(sent()).toHaveLength(1));
  saver.choose(durationQuestion.id, [thirtySeconds]);
  first.release(saved());

  await waitFor(() => expect(sent()).toHaveLength(2));
  expect(saver.draftOf(durationQuestion.id)).toEqual([thirtySeconds]);
  expect(cachedChoice(queryClient, durationQuestion.id)).not.toEqual([fiveSeconds]);

  second.release(saved());
  await waitFor(() => expect(cachedChoice(queryClient, durationQuestion.id)).toEqual([thirtySeconds]));
});

test("a saved answer goes into the examination's cache and its draft is dropped, without reading again", async () => {
  const { saver, queryClient } = renderSaver();
  const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

  saver.choose(protectionQuestion.id, [coat, gloves]);
  expect(saver.draftOf(protectionQuestion.id)).toEqual([coat, gloves]);

  await waitFor(() => expect(cachedChoice(queryClient, protectionQuestion.id)).toEqual([coat, gloves]));
  expect(saver.draftOf(protectionQuestion.id)).toBeUndefined();
  expect(saver.stateOf(protectionQuestion.id)).toEqual({ kind: 'saved' });
  // The other question is as it was.
  expect(cachedChoice(queryClient, durationQuestion.id)).toEqual([]);
  expect(invalidate).not.toHaveBeenCalled();
});

test('a 409 examination-changed is sent again by itself, and the answer is saved', async () => {
  let calls = 0;
  answering = async () => {
    calls += 1;
    return calls <= 2 ? refusal(409, 'assessment.examination-changed') : saved();
  };
  const { saver } = renderSaver();

  saver.choose(durationQuestion.id, [thirtySeconds]);

  await waitFor(() => expect(saver.stateOf(durationQuestion.id)).toEqual({ kind: 'saved' }));
  expect(sent()).toHaveLength(3);
  expect(new Set(sent().map(({ body }) => JSON.stringify(body)))).toEqual(
    new Set([JSON.stringify({ optionIds: [thirtySeconds] })]),
  );
});

// A save PostgreSQL ended as a deadlock with another (#162): nothing was stored, so it is sent again like a changed one.
test('a 409 concurrent-save is sent again by itself, and the answer is saved', async () => {
  let calls = 0;
  answering = async () => {
    calls += 1;
    return calls <= 2 ? refusal(409, 'assessment.concurrent-save') : saved();
  };
  const { saver } = renderSaver();

  saver.choose(durationQuestion.id, [thirtySeconds]);

  await waitFor(() => expect(saver.stateOf(durationQuestion.id)).toEqual({ kind: 'saved' }));
  expect(sent()).toHaveLength(3);
});

test('examination-changed is sent again up to 3 times, then the save has failed and the draft stays', async () => {
  answering = async () => refusal(409, 'assessment.examination-changed');
  const { saver } = renderSaver();

  saver.choose(durationQuestion.id, [thirtySeconds]);

  await waitFor(() =>
    expect(saver.stateOf(durationQuestion.id)).toEqual({
      kind: 'failed',
      failure: { key: 'assessment.test.changed', values: {} },
    }),
  );
  // The first request and three more.
  expect(sent()).toHaveLength(4);
  expect(saver.draftOf(durationQuestion.id)).toEqual([thirtySeconds]);
});

test('a failed save keeps the draft, and retry saves it again', async () => {
  answering = async () => new Response('', { status: 503 });
  const { saver, queryClient } = renderSaver();

  saver.choose(durationQuestion.id, [thirtySeconds]);
  await waitFor(() =>
    expect(saver.stateOf(durationQuestion.id)).toEqual({
      kind: 'failed',
      failure: { key: 'assessment.test.save.failed', values: {} },
    }),
  );
  expect(sent()).toHaveLength(1);
  expect(saver.draftOf(durationQuestion.id)).toEqual([thirtySeconds]);

  answering = async () => saved();
  saver.retry(durationQuestion.id);

  await waitFor(() => expect(saver.stateOf(durationQuestion.id)).toEqual({ kind: 'saved' }));
  expect(sent()).toHaveLength(2);
  expect(cachedChoice(queryClient, durationQuestion.id)).toEqual([thirtySeconds]);
});

test.each([
  ['question-not-in-attempt', 404, 'assessment.test.save.reload'],
  ['option-not-in-question', 422, 'assessment.test.save.reload'],
  ['single-choice-takes-one-option', 422, 'assessment.test.save.singleChoice'],
])('a refused %s fails the save with its own text, without sending again', async (code, status, key) => {
  answering = async () => refusal(status, `assessment.${code}`);
  const { saver } = renderSaver();

  saver.choose(durationQuestion.id, [thirtySeconds]);

  await waitFor(() =>
    expect(saver.stateOf(durationQuestion.id)).toEqual({ kind: 'failed', failure: { key, values: {} } }),
  );
  expect(sent()).toHaveLength(1);
});

test('a 409 no-open-attempt stops the saver: it is closed, the read is invalidated, and nothing is sent any more', async () => {
  answering = async () => refusal(409, 'assessment.no-open-attempt');
  const { saver, queryClient } = renderSaver();
  const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

  saver.choose(durationQuestion.id, [thirtySeconds]);

  await waitFor(() => expect(saver.closed.value).toBe(true));
  expect(invalidate).toHaveBeenCalledWith(expect.objectContaining({ queryKey: examinationKey }));
  saver.choose(protectionQuestion.id, [coat]);
  await saver.settled();
  expect(sent()).toHaveLength(1);
});

test('unchecking the last option of a multiple-choice question sends nothing and says one must stay chosen', async () => {
  const { saver } = renderSaver();

  saver.choose(protectionQuestion.id, []);

  expect(saver.stateOf(protectionQuestion.id)).toEqual({ kind: 'keepOne' });
  await saver.settled();
  expect(sent()).toEqual([]);
});

test('settled() waits for the save on its way and says every answer is saved', async () => {
  const first = deferred();
  answering = () => first.answer;
  const { saver } = renderSaver();
  saver.choose(durationQuestion.id, [thirtySeconds]);
  await waitFor(() => expect(sent()).toHaveLength(1));

  const log: string[] = [];
  let done: boolean | undefined;
  void saver.settled().then((all: boolean) => {
    log.push('settled');
    done = all;
  });

  log.push('answered');
  first.release(saved());

  await waitFor(() => expect(done).toBe(true));
  expect(log).toEqual(['answered', 'settled']);
});

test('settled() says not every answer is saved once a save has failed', async () => {
  answering = async () => new Response('', { status: 503 });
  const { saver } = renderSaver();

  saver.choose(durationQuestion.id, [thirtySeconds]);

  expect(await saver.settled()).toBe(false);
});

test('settled() with nothing to save says every answer is saved', async () => {
  const { saver } = renderSaver();

  expect(await saver.settled()).toBe(true);
});

test('unchecking the last option of an answer that failed to save keeps the failure, and settled() says it is not saved', async () => {
  answering = async () => new Response('', { status: 503 });
  const { saver } = renderSaver();
  saver.choose(protectionQuestion.id, [coat]);
  await waitFor(() => expect(saver.stateOf(protectionQuestion.id).kind).toBe('failed'));

  saver.choose(protectionQuestion.id, []);

  expect(saver.stateOf(protectionQuestion.id).kind).toBe('failed');
  expect(saver.draftOf(protectionQuestion.id)).toEqual([coat]);
  expect(await saver.settled()).toBe(false);
  expect(sent()).toHaveLength(1);
});
