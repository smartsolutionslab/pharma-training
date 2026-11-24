<script setup lang="ts">
import { BookOpen, CircleCheck } from '@lucide/vue';
import { computed, nextTick, onBeforeUnmount, ref, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { type RouteLocationRaw, RouterLink, useRoute, useRouter } from 'vue-router';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-state';
import { Skeleton } from '@/components/ui/skeleton';
import PdfChapter from './PdfChapter.vue';
import TextChapter from './TextChapter.vue';
import { type PlayerChapter, useCoursePlayer } from './useCoursePlayer';
import { useProgressReporter } from './useProgressReporter';
import VideoChapter from './VideoChapter.vue';

// The chapter player (#44) at /learning/courses/:courseVersionId: the course's title, its chapters in order with their
// kind and state, and the chapter shown (?chapter=). Progress goes to the host only; what it answers is what is shown.
const { t, n } = useI18n();
const { version, sessionId, chapters, current, contentCompleted, failure, retryable, loading, retry } =
  useCoursePlayer();

/**
 * What the polite status region says: the latest few, so two in a row are both heard. Each keeps an id of its own, so
 * a message stays the element it was while newer ones come and older ones go, and is never heard again.
 */
const announcements = ref<{ id: number; text: string }[]>([]);
let announced = 0;
function announce(text: string) {
  announced += 1;
  announcements.value = [...announcements.value.slice(-2), { id: announced, text }];
}
const reporter = useProgressReporter(sessionId, {
  titleOf: (chapterId) => chapters.value.find((chapter) => chapter.chapterId === chapterId)?.title ?? '',
  announce,
});
const { failed: reportFailed, finished: sessionFinished } = reporter;

/** The longest a navigation waits for the pending report's answer; the report is on its way either way. */
const flushWait = 2_000;

/** Sends what is pending and resolves once it is answered, or after flushWait at the latest. */
async function flushBeforeLeaving() {
  let timeout: ReturnType<typeof setTimeout> | undefined;
  try {
    await Promise.race([
      reporter.flush(),
      new Promise<void>((resolve) => {
        timeout = setTimeout(resolve, flushWait);
      }),
    ]);
  } finally {
    clearTimeout(timeout);
  }
}

// A chapter left (?chapter= changes) or the page left reports what was pending first. A guard of the router's own,
// since the page may be rendered without a RouterView, where in-component guards never run.
const router = useRouter();
const route = useRoute();
const removeGuard = router.beforeEach(async (to, from) => {
  if (to.fullPath !== from.fullPath) {
    await flushBeforeLeaving();
  }
});
onBeforeUnmount(removeGuard);

/** The address of a chapter: this page with it in ?chapter=. */
const chapterLocation = (chapter: PlayerChapter): RouteLocationRaw => ({
  query: { ...route.query, chapter: chapter.chapterId },
});
const currentIndex = computed(() =>
  chapters.value.findIndex((chapter) => chapter.chapterId === current.value?.chapterId),
);
const previousChapter = computed(() => (currentIndex.value > 0 ? chapters.value[currentIndex.value - 1] : undefined));
const nextChapter = computed(() => (currentIndex.value >= 0 ? chapters.value[currentIndex.value + 1] : undefined));

function openChapter(chapter: PlayerChapter | undefined) {
  if (chapter !== undefined) {
    void router.push(chapterLocation(chapter));
  }
}

const chapterHeading = useTemplateRef<HTMLElement>('chapterHeading');
// The chapter's heading takes the focus once another chapter shows, so the learner reads on from its title.
watch(
  () => current.value?.chapterId,
  (_chapterId, previous) => {
    if (previous !== undefined) {
      chapterHeading.value?.focus();
    }
  },
  { flush: 'post' },
);

function reportProgress(chapter: PlayerChapter, percent: number) {
  reporter.report(chapter.chapterId, percent);
}

function trackProgress(chapter: PlayerChapter, percent: number) {
  reporter.track(chapter.chapterId, percent);
}

function stateOf(chapter: PlayerChapter): string {
  if (chapter.completed) {
    return t('learning.player.state.done');
  }
  if (chapter.progressPercent > 0) {
    return t('learning.player.state.progress', { percent: n(chapter.progressPercent / 100, 'percent') });
  }
  return t('learning.player.state.open');
}

const heading = useTemplateRef<HTMLElement>('heading');
/** Set by Retry: the course's heading takes the focus once it shows. */
const focusHeading = ref(false);
watch(
  [focusHeading, heading],
  async ([wanted, element]) => {
    if (!wanted || element === null) {
      return;
    }
    await nextTick();
    focusHeading.value = false;
    element.focus();
  },
  { flush: 'post' },
);

function retryLoading() {
  focusHeading.value = true;
  retry();
}

const headingId = 'course-player-title';
const chapterHeadingId = 'course-player-chapter';
</script>

<template>
  <div class="flex flex-col gap-6" :aria-busy="loading ? 'true' : undefined">
    <div v-if="failure !== undefined" role="alert" class="flex flex-col items-start gap-3">
      <p class="text-body">{{ t(`learning.player.${failure}`) }}</p>
      <Button v-if="retryable" variant="outline" @click="retryLoading">{{ t('shell.retry') }}</Button>
    </div>

    <template v-else-if="loading">
      <Skeleton class="h-8 w-2/3" />
      <div class="grid gap-6 md:grid-cols-[16rem_1fr]">
        <div class="flex flex-col gap-2">
          <Skeleton v-for="row in 3" :key="row" class="h-10 w-full" />
        </div>
        <Skeleton class="h-48 w-full" />
      </div>
    </template>

    <template v-else-if="version !== undefined">
      <h1 :id="headingId" ref="heading" tabindex="-1" class="text-title focus:outline-none">{{ version.title }}</h1>

      <div role="status" class="sr-only">
        <p v-for="announcement in announcements" :key="announcement.id">{{ announcement.text }}</p>
      </div>

      <div v-if="sessionFinished" role="alert" class="text-body">{{ t('learning.player.progress.finished') }}</div>
      <div v-else-if="reportFailed" role="alert" class="flex flex-col items-start gap-3">
        <p class="text-body">{{ t('learning.player.progress.failed') }}</p>
        <Button variant="outline" @click="reporter.flush()">{{ t('learning.player.progress.saveNow') }}</Button>
      </div>

      <!-- A lasting panel, no second status region: the polite region above has said it once already. -->
      <section v-if="contentCompleted" class="bg-card text-body shadow-elevation-1 rounded-md p-4">
        <p>{{ t('learning.player.contentCompleted') }}</p>
      </section>

      <EmptyState v-if="chapters.length === 0">
        <template #icon><BookOpen /></template>
        {{ t('learning.player.empty') }}
      </EmptyState>

      <div v-else class="grid gap-6 md:grid-cols-[16rem_1fr]">
        <nav :aria-label="t('learning.player.chapters')">
          <ol class="flex flex-col gap-2">
            <li
              v-for="chapter in chapters"
              :key="chapter.chapterId"
              :aria-current="chapter.chapterId === current?.chapterId ? 'step' : undefined"
            >
              <!-- custom: the item says which chapter is current (step), not the link that every query here matches. -->
              <RouterLink v-slot="{ href, navigate }" :to="chapterLocation(chapter)" custom>
                <a
                  :href="href"
                  :class="[
                    'hover:bg-accent focus-visible:ring-ring flex flex-col gap-1 rounded-md border p-3 focus-visible:ring-2 focus-visible:outline-none motion-safe:transition-colors',
                    chapter.chapterId === current?.chapterId ? 'border-primary bg-accent' : 'border-border',
                  ]"
                  @click="navigate"
                >
                  <span class="text-body">{{ chapter.title }}</span>
                  <span class="text-caption text-muted-foreground flex flex-wrap items-center gap-2">
                    <span>{{ t(`glossary.chapterKind.${chapter.kind}`) }}</span>
                    <span class="inline-flex items-center gap-1 tabular-nums">
                      <CircleCheck v-if="chapter.completed" class="size-4" aria-hidden="true" />{{ stateOf(chapter) }}
                    </span>
                  </span>
                </a>
              </RouterLink>
            </li>
          </ol>
        </nav>

        <section v-if="current !== undefined" :aria-labelledby="chapterHeadingId" class="flex flex-col gap-4">
          <div class="flex flex-wrap items-center gap-3">
            <h2
              :id="chapterHeadingId"
              ref="chapterHeading"
              tabindex="-1"
              class="text-body font-semibold focus:outline-none"
            >
              {{ current.title }}
            </h2>
            <span
              v-if="current.completed"
              class="bg-secondary text-secondary-foreground text-caption inline-flex items-center gap-1 rounded-md px-2 py-0.5"
            >
              <CircleCheck class="size-4" aria-hidden="true" />{{ t('learning.player.state.done') }}
            </span>
          </div>
          <TextChapter
            v-if="current.kind === 'Text'"
            :key="current.chapterId"
            :text="current.text ?? ''"
            :completed="current.completed"
            @progress="(percent) => current !== undefined && reportProgress(current, percent)"
          />
          <VideoChapter
            v-else-if="current.kind === 'Video'"
            :key="current.chapterId"
            :chapter-id="current.chapterId"
            :title="current.title"
            :duration-seconds="current.videoDurationSeconds ?? 0"
            :progress-percent="current.progressPercent"
            :completed="current.completed"
            @progress="(percent) => current !== undefined && reportProgress(current, percent)"
            @tracking="(percent) => current !== undefined && trackProgress(current, percent)"
            @announce="announce"
          />
          <PdfChapter
            v-else-if="current.kind === 'Pdf'"
            :key="current.chapterId"
            :chapter-id="current.chapterId"
            :title="current.title"
            :progress-percent="current.progressPercent"
            :completed="current.completed"
            @progress="(percent) => current !== undefined && reportProgress(current, percent)"
          />
          <div class="flex flex-wrap justify-between gap-3">
            <Button variant="outline" :disabled="previousChapter === undefined" @click="openChapter(previousChapter)">
              {{ t('learning.player.previous') }}
            </Button>
            <Button :disabled="nextChapter === undefined" @click="openChapter(nextChapter)">
              {{ t('learning.player.next') }}
            </Button>
          </div>
        </section>
      </div>
    </template>
  </div>
</template>
