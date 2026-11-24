<script setup lang="ts">
import { CircleCheck, CircleX } from '@lucide/vue';
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRoute } from 'vue-router';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { assessmentFailureText } from './assessmentErrors';
import { useExamination } from './useExamination';

// The result page (#48) at /assessment/sessions/:sessionId/result: the examination's lastResult, read by session, so a
// reload shows it again. The score and the pass mark as percentages, the attempt and the attempts left; after a failure
// the chapters to repeat in the course's order, each linked to its chapter in the player, and the lock once every
// attempt is used (ADR 0023/0025).
const { t, n } = useI18n();
const route = useRoute();

const sessionId = computed(() => String(route.params.sessionId ?? ''));
const { examination, version, catalogFailed, retryCatalog, notYetThere, failureOf, retryable, loading, refetch } =
  useExamination(sessionId);
const failure = failureOf('result');
const testAddress = computed(() => `/assessment/sessions/${sessionId.value}`);

const result = computed(() => examination.value?.lastResult ?? undefined);
const locked = computed(() => examination.value?.status === 'Exhausted');
const attemptsLeft = computed(() => Number(result.value?.attemptsLeft ?? 0));
const showAttemptsLeft = computed(() => examination.value?.status === 'Unlocked' && attemptsLeft.value > 0);

const title = computed(() =>
  version.value === undefined
    ? t('modules.assessment.details.result.title')
    : t('assessment.result.heading', { title: version.value.title }),
);
const score = computed(() =>
  result.value === undefined
    ? ''
    : t('assessment.result.score', {
        score: n(Number(result.value.scorePercent) / 100, 'percent'),
        passing: n(Number(result.value.passingScorePercent) / 100, 'percent'),
      }),
);

/** A chapter to repeat: by its title, linked to the player, or a chapter the catalog does not name. */
interface ChapterToRepeat {
  chapterId: string;
  title?: string;
  position: number;
}
const chaptersToRepeat = computed((): ChapterToRepeat[] => {
  const known = new Map((version.value?.chapters ?? []).map((chapter) => [chapter.chapterId, chapter]));
  return (result.value?.chaptersToRepeat ?? [])
    .map((chapterId): ChapterToRepeat => {
      const chapter = known.get(chapterId);
      return chapter === undefined
        ? { chapterId, position: Number.MAX_SAFE_INTEGER }
        : { chapterId, title: chapter.title, position: Number(chapter.position) };
    })
    .sort((first, second) => first.position - second.position);
});
const chapterAddress = (chapterId: string) =>
  `/learning/courses/${examination.value?.courseVersionId ?? ''}?chapter=${chapterId}`;

const heading = useTemplateRef<HTMLElement>('heading');
/** The heading takes the focus once it first shows, and again after Retry. */
const focusHeading = ref(true);
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
  void refetch();
}

const headingId = 'examination-result-title';
</script>

<template>
  <div class="flex flex-col gap-6" :aria-busy="loading ? 'true' : undefined">
    <div v-if="failure !== undefined" role="alert" class="flex flex-col items-start gap-3">
      <p class="text-body">{{ assessmentFailureText(failure) }}</p>
      <Button v-if="retryable" variant="outline" @click="retryLoading">{{ t('shell.retry') }}</Button>
    </div>

    <template v-else-if="loading">
      <Skeleton class="h-8 w-2/3" />
      <Skeleton class="h-24 w-full" />
      <Skeleton class="h-16 w-full" />
    </template>

    <section v-else-if="notYetThere" class="flex flex-col items-start gap-3">
      <p class="text-body">{{ t('assessment.test.notUnlocked') }}</p>
      <RouterLink to="/learning" class="text-body text-primary underline underline-offset-4">
        {{ t('assessment.test.toCourses') }}
      </RouterLink>
    </section>

    <template v-else-if="examination !== undefined">
      <h1 :id="headingId" ref="heading" tabindex="-1" class="text-title focus:outline-none">{{ title }}</h1>

      <section v-if="result === undefined" class="flex flex-col items-start gap-3">
        <p class="text-body">{{ t('assessment.result.none') }}</p>
        <RouterLink :to="testAddress" class="text-body text-primary underline underline-offset-4">
          {{ t('assessment.result.toTest') }}
        </RouterLink>
      </section>

      <template v-else>
        <section class="bg-card shadow-elevation-1 flex flex-col items-start gap-3 rounded-md border p-4">
          <span
            :class="[
              'text-body inline-flex items-center gap-1 rounded-md px-2 py-0.5 font-semibold',
              result.outcome === 'Passed'
                ? 'bg-primary text-primary-foreground'
                : 'bg-secondary text-secondary-foreground',
            ]"
          >
            <CircleCheck v-if="result.outcome === 'Passed'" class="size-4" aria-hidden="true" />
            <CircleX v-else class="size-4" aria-hidden="true" />
            <span>{{ t(`assessment.result.outcome.${result.outcome}`) }}</span>
          </span>
          <p class="text-body tabular-nums">{{ score }}</p>
          <p class="text-body tabular-nums">{{ t('assessment.result.attempt', { number: Number(result.number) }) }}</p>
          <p v-if="showAttemptsLeft" class="text-body tabular-nums">
            {{ t('assessment.result.attemptsLeft', { count: attemptsLeft }, attemptsLeft) }}
          </p>
        </section>

        <section v-if="result.outcome === 'Passed'" class="flex flex-col items-start gap-3">
          <p class="text-body">{{ t('assessment.result.passedNext') }}</p>
          <RouterLink to="/learning" class="text-body text-primary underline underline-offset-4">
            {{ t('assessment.test.toCourses') }}
          </RouterLink>
        </section>

        <section v-else class="flex flex-col items-start gap-3">
          <p class="text-body">
            {{ t(locked ? 'assessment.result.locked' : 'assessment.result.repeatRecommended') }}
          </p>
          <div v-if="catalogFailed" role="alert" class="flex flex-col items-start gap-3">
            <p class="text-body">{{ t('assessment.result.chaptersFailed') }}</p>
            <Button variant="outline" @click="retryCatalog">{{ t('shell.retry') }}</Button>
          </div>
          <div v-else-if="version === undefined" class="flex w-full flex-col gap-2">
            <Skeleton v-for="row in chaptersToRepeat.length" :key="row" class="h-6 w-1/2" />
          </div>
          <ol v-else class="text-body flex list-decimal flex-col gap-1 pl-6">
            <li v-for="chapter in chaptersToRepeat" :key="chapter.chapterId">
              <RouterLink
                v-if="chapter.title !== undefined"
                :to="chapterAddress(chapter.chapterId)"
                class="text-primary underline underline-offset-4"
              >
                {{ chapter.title }}
              </RouterLink>
              <span v-else>{{ t('assessment.result.unknownChapter') }}</span>
            </li>
          </ol>
          <RouterLink v-if="!locked" :to="testAddress" class="text-body text-primary underline underline-offset-4">
            {{ t('assessment.result.retry') }}
          </RouterLink>
        </section>
      </template>
    </template>
  </div>
</template>
