import { useQueryClient } from '@tanstack/vue-query';
import { type MaybeRefOrGetter, reactive, ref, toValue } from 'vue';
import { ApiError } from '@/api/baseApi';
import {
  type AssessmentExaminationDto,
  getGetAssessmentExaminationsBySessionSessionIdQueryKey,
  putAssessmentExaminationsIdAttemptsCurrentAnswersQuestionId,
} from '@/api/generated';
import { type AssessmentFailure, assessmentFailure } from './assessmentErrors';

/** Where a question's answer stands: not touched, on its way, saved, refused as empty, or failed. */
export type SaveState =
  { kind: 'idle' | 'saving' | 'saved' | 'keepOne' } | { kind: 'failed'; failure: AssessmentFailure };

/** How often a 409 examination-changed is sent again by itself (ADR 0016: sending again is safe). */
const resends = 3;

const idle: SaveState = { kind: 'idle' };

/**
 * Saves the learner's answers (#48) with PUT …/attempts/current/answers/{questionId}, one request at a time per
 * examination, since concurrent answers would race into examination-changed. Each choice is the question's draft (client
 * state) until the host has saved it; then it goes into the by-session query's cache and the draft is dropped. While a
 * request is on its way only each question's latest draft waits, so repeated clicks send once. An empty set is never
 * sent (an answer cannot be cleared); a 409 no-open-attempt closes the saver and reads the examination again.
 */
export function useAnswerSaver(
  sessionId: MaybeRefOrGetter<string>,
  examinationId: MaybeRefOrGetter<string>,
  { onSaved }: { onSaved?: (questionId: string) => void } = {},
) {
  const queryClient = useQueryClient();
  const examinationKey = () => getGetAssessmentExaminationsBySessionSessionIdQueryKey(toValue(sessionId));

  const drafts = reactive(new Map<string, string[]>());
  const states = reactive(new Map<string, SaveState>());
  /** Whether the attempt was submitted (here or elsewhere): nothing is sent any more. */
  const closed = ref(false);

  /** The questions whose drafts wait to be sent, in the order they were chosen. */
  let waiting: string[] = [];
  let busy = false;
  let settledWaiters: (() => void)[] = [];

  const stateOf = (questionId: string): SaveState => states.get(questionId) ?? idle;
  const draftOf = (questionId: string): string[] | undefined => drafts.get(questionId);

  function enqueue(questionId: string) {
    states.set(questionId, { kind: 'saving' });
    if (!waiting.includes(questionId)) {
      waiting.push(questionId);
    }
    void pump();
  }

  /** Keeps the choice as the question's draft and saves it; an empty choice is never sent. */
  function choose(questionId: string, optionIds: string[]) {
    if (closed.value) {
      return;
    }
    if (optionIds.length === 0) {
      states.set(questionId, { kind: 'keepOne' });
      return;
    }
    drafts.set(questionId, [...optionIds]);
    enqueue(questionId);
  }

  /** Saves the question's draft again, after its save failed. */
  function retry(questionId: string) {
    if (closed.value || !drafts.has(questionId)) {
      return;
    }
    enqueue(questionId);
  }

  function remember(questionId: string, optionIds: string[]) {
    queryClient.setQueryData<AssessmentExaminationDto>(examinationKey(), (read) => {
      const attempt = read?.currentAttempt;
      if (read === undefined || attempt === null || attempt === undefined) {
        return read;
      }
      return {
        ...read,
        currentAttempt: {
          ...attempt,
          questions: attempt.questions.map((question) =>
            question.id === questionId ? { ...question, chosenOptionIds: optionIds } : question,
          ),
        },
      };
    });
  }

  async function send(questionId: string) {
    for (let attempt = 0; ; attempt += 1) {
      const draft = drafts.get(questionId);
      if (draft === undefined) {
        return;
      }
      try {
        await putAssessmentExaminationsIdAttemptsCurrentAnswersQuestionId(toValue(examinationId), questionId, {
          optionIds: draft,
        });
      } catch (error) {
        if (error instanceof ApiError && error.code === 'assessment.examination-changed' && attempt < resends) {
          continue;
        }
        fail(questionId, draft, error);
        return;
      }
      // A draft changed while its save was on its way waits to be sent; the cache never takes the older choice.
      if (drafts.get(questionId) === draft) {
        remember(questionId, draft);
        drafts.delete(questionId);
        states.set(questionId, { kind: 'saved' });
        onSaved?.(questionId);
      }
      return;
    }
  }

  function fail(questionId: string, draft: string[], error: unknown) {
    if (error instanceof ApiError && error.code === 'assessment.no-open-attempt') {
      closed.value = true;
      waiting = [];
      void queryClient.invalidateQueries({ queryKey: examinationKey() });
    }
    if (drafts.get(questionId) === draft || closed.value) {
      states.set(questionId, { kind: 'failed', failure: assessmentFailure('answer', error) });
    }
  }

  async function pump() {
    if (busy) {
      return;
    }
    busy = true;
    try {
      for (let next = waiting.shift(); next !== undefined && !closed.value; next = waiting.shift()) {
        await send(next);
      }
    } finally {
      busy = false;
      const waiters = settledWaiters;
      settledWaiters = [];
      for (const resolve of waiters) {
        resolve();
      }
    }
  }

  const allSaved = () => [...states.values()].every((state) => state.kind !== 'failed');

  /** Resolves once nothing waits or is on its way: true when every answer is saved, false when one failed. */
  function settled(): Promise<boolean> {
    if (!busy && (waiting.length === 0 || closed.value)) {
      return Promise.resolve(allSaved());
    }
    return new Promise<boolean>((resolve) => {
      settledWaiters.push(() => resolve(allSaved()));
    });
  }

  return { choose, retry, draftOf, stateOf, settled, closed };
}
