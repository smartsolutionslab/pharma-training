import { useQueryClient } from '@tanstack/vue-query';
import { getCurrentScope, type MaybeRefOrGetter, onScopeDispose, type Ref, ref, toValue, watch } from 'vue';
import { ApiError } from '@/api/baseApi';
import {
  getGetLearningSessionsIdQueryKey,
  type LearningSessionDto,
  postLearningSessionsIdProgress,
} from '@/api/generated';
import { t } from '@/i18n';

export interface ProgressReporterOptions {
  /** The chapter's title, for the announcement that it is done. */
  titleOf: (chapterId: string) => string;
  /** Says a text in the page's polite status region. */
  announce: (text: string) => void;
}

export interface ProgressReporter {
  /** Sends the chapter's percent at once: a pause, the chapter's end, a chapter change. */
  report: (chapterId: string, percent: number) => void;
  /** Progress while playing: kept pending and sent at most every 30 seconds. */
  track: (chapterId: string, percent: number) => void;
  /** Sends what is pending now ("Save now"); resolves once it has been answered. */
  flush: () => Promise<void>;
  /** Whether the last report failed; what it carried stays pending. */
  failed: Ref<boolean>;
  /** Whether the session is finished: nothing is reported any more. */
  finished: Ref<boolean>;
}

/** The longest a tracked percent waits before it is sent. */
const trackingInterval = 30_000;

/**
 * Reports a learner's progress on the session's chapters to POST /learning/sessions/{id}/progress (#44). The server is
 * the only record: nothing is kept in the browser's storage. Only a percent above the one the host acknowledged is
 * sent (read from the session's cached answer, which every 200 replaces), one request at a time, and while one is on
 * its way only the highest percent per chapter waits. Hiding or leaving the page sends what waits with keepalive, and
 * the report on its way too, which the page may cut off: it counts as sent only once the host has answered. What waits
 * belongs to its session: when the session changes (another course version), it is dropped.
 */
export function useProgressReporter(
  sessionId: MaybeRefOrGetter<string>,
  { titleOf, announce }: ProgressReporterOptions,
): ProgressReporter {
  const queryClient = useQueryClient();
  const failed = ref(false);
  const finished = ref(false);
  /** The highest percent per chapter not yet sent, or sent and not stored. */
  const pending = new Map<string, number>();
  /** The percent per chapter on its way, until the host has answered it. */
  const inFlight = new Map<string, number>();
  let timer: ReturnType<typeof setTimeout> | undefined;
  /** Whether a loop is sending what waits; it clears the moment the loop ends, not a tick later. */
  let draining = false;
  let run: Promise<void> = Promise.resolve();
  let disposed = false;

  const sessionKey = (id = toValue(sessionId)) => getGetLearningSessionsIdQueryKey(id);
  const cached = (id = toValue(sessionId)) => queryClient.getQueryData<LearningSessionDto>(sessionKey(id));
  const acknowledged = (chapterId: string) =>
    Number(cached()?.chapters.find((chapter) => chapter.chapterId === chapterId)?.progressPercent ?? 0);

  function keep(chapterId: string, percent: number) {
    if (percent <= acknowledged(chapterId)) {
      return;
    }
    pending.set(chapterId, Math.max(pending.get(chapterId) ?? 0, percent));
  }

  /** The next pending percent worth sending, taken out of what waits; one at or below the acknowledged is dropped. */
  function takeNext(): [string, number] | undefined {
    for (const [chapterId, percent] of pending) {
      pending.delete(chapterId);
      if (percent > acknowledged(chapterId)) {
        return [chapterId, percent];
      }
    }
    return undefined;
  }

  /**
   * The host's answer is the stored session: it goes into the cache under its own session's key, and what became done
   * is announced, while that session is still the one reported on.
   */
  function stored(id: string, answer: LearningSessionDto) {
    const before = cached(id);
    queryClient.setQueryData(sessionKey(id), answer);
    if (disposed || id !== toValue(sessionId)) {
      return;
    }
    failed.value = false;
    const doneBefore = new Set(
      (before?.chapters ?? []).filter((chapter) => chapter.completed).map((chapter) => chapter.chapterId),
    );
    for (const chapter of answer.chapters) {
      if (chapter.completed && !doneBefore.has(chapter.chapterId)) {
        announce(t('learning.player.chapterDone', { title: titleOf(chapter.chapterId) }));
      }
    }
    if (answer.status === 'ContentCompleted' && before?.status !== 'ContentCompleted') {
      announce(t('learning.player.contentCompleted'));
    }
  }

  /** Sends one percent; whether the host stored it. A session that changed meanwhile is read again and sent once more. */
  async function send(chapterId: string, percent: number, keepalive: boolean, attempt = 1): Promise<boolean> {
    const id = toValue(sessionId);
    if (!keepalive) {
      inFlight.set(chapterId, percent);
    }
    try {
      const answer = await postLearningSessionsIdProgress(
        id,
        { chapterId, percent },
        keepalive ? { keepalive: true } : undefined,
      );
      stored(id, answer);
      return true;
    } catch (error) {
      if (id !== toValue(sessionId)) {
        // The session changed meanwhile: what this one answered is not the current one's to show.
        return false;
      }
      const code = error instanceof ApiError ? (error.code ?? error.failureCode) : undefined;
      if (code === 'learning.session-finished') {
        finished.value = true;
        failed.value = false;
        pending.clear();
        return false;
      }
      if (code === 'learning.session-changed' && attempt === 1) {
        await queryClient.invalidateQueries({ queryKey: sessionKey(id) });
        if (id === toValue(sessionId) && percent > acknowledged(chapterId)) {
          return send(chapterId, percent, keepalive, attempt + 1);
        }
        return true;
      }
      pending.set(chapterId, Math.max(pending.get(chapterId) ?? 0, percent));
      failed.value = true;
      return false;
    } finally {
      if (!keepalive && inFlight.get(chapterId) === percent) {
        inFlight.delete(chapterId);
      }
    }
  }

  async function runLoop() {
    try {
      for (;;) {
        const next = finished.value ? undefined : takeNext();
        if (next === undefined) {
          return;
        }
        if (!(await send(next[0], next[1], false))) {
          return;
        }
      }
    } finally {
      draining = false;
    }
  }

  /** Sends what waits, one request at a time; a call while one is on its way joins it. */
  function drain(): Promise<void> {
    if (!draining) {
      draining = true;
      run = runLoop();
    }
    return run;
  }

  /**
   * Sends everything that waits at once with keepalive, so the request outlives the page, and the report on its way
   * again: a request without keepalive may be cut off with the page.
   */
  function sendWithKeepalive() {
    if (finished.value) {
      return;
    }
    for (const [chapterId, percent] of inFlight) {
      keep(chapterId, percent);
    }
    for (let next = takeNext(); next !== undefined; next = takeNext()) {
      void send(next[0], next[1], true);
    }
  }

  // What waits was meant for the session it was reported on; another session (another course version) starts empty.
  watch(
    () => toValue(sessionId),
    () => {
      pending.clear();
      inFlight.clear();
      failed.value = false;
      if (timer !== undefined) {
        clearTimeout(timer);
        timer = undefined;
      }
    },
  );

  function report(chapterId: string, percent: number) {
    if (finished.value) {
      return;
    }
    keep(chapterId, percent);
    void drain();
  }

  function track(chapterId: string, percent: number) {
    if (finished.value) {
      return;
    }
    keep(chapterId, percent);
    if (timer === undefined) {
      timer = setTimeout(() => {
        timer = undefined;
        void drain();
      }, trackingInterval);
    }
  }

  function flush(): Promise<void> {
    if (finished.value) {
      return Promise.resolve();
    }
    return drain();
  }

  const onVisibilityChange = () => {
    if (document.visibilityState === 'hidden') {
      sendWithKeepalive();
    }
  };
  document.addEventListener('visibilitychange', onVisibilityChange);
  window.addEventListener('pagehide', sendWithKeepalive);

  if (getCurrentScope() !== undefined) {
    onScopeDispose(() => {
      document.removeEventListener('visibilitychange', onVisibilityChange);
      window.removeEventListener('pagehide', sendWithKeepalive);
      if (timer !== undefined) {
        clearTimeout(timer);
        timer = undefined;
      }
      sendWithKeepalive();
      disposed = true;
    });
  }

  return { report, track, flush, failed, finished };
}
