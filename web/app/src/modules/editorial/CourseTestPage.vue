<script setup lang="ts">
import { ListChecks } from '@lucide/vue';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRoute } from 'vue-router';
import { z } from 'zod';
import { useGetEditorialCourseVersionsId } from '@/api/generated';
import { ApiError } from '@/api/baseApi';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-state';
import { Skeleton } from '@/components/ui/skeleton';
import QuestionCard from './QuestionCard.vue';
import TestBlueprintSection from './TestBlueprintSection.vue';
import { testCounts } from './testCounts';

// The test of a course version (/editorial/versions/:id/test), a page of its own beside the version's: headed by the
// version's title, number and status, with the way back to the version. Only a draft's test can be changed; any other
// status says why not and shows the test read-only.
const route = useRoute();
const id = computed(() => String(route.params.id ?? ''));
/** The id goes into the requests' paths: one that is no UUID names no version, and is never sent. */
const validId = computed(() => z.uuid().safeParse(id.value).success);
const {
  data: version,
  isPending,
  isError,
  error,
} = useGetEditorialCourseVersionsId(id, { query: { enabled: validId } });
const { t, n } = useI18n();

const notFound = computed(() => error.value instanceof ApiError && error.value.status === 404);
const editable = computed(() => version.value?.status === 'Draft');
/** The questions by position, as the test asks them. */
const questions = computed(() =>
  [...(version.value?.questions ?? [])].sort((a, b) => Number(a.position) - Number(b.position)),
);
const chapters = computed(() => new Map((version.value?.chapters ?? []).map((chapter) => [chapter.id, chapter])));
const counts = computed(() =>
  version.value === undefined
    ? undefined
    : testCounts({ questions: questions.value, blueprint: version.value.blueprint }),
);
</script>

<template>
  <main class="flex flex-col gap-6">
    <p v-if="!validId" class="text-body">{{ t('editorial.version.notFound') }}</p>
    <template v-else-if="isPending">
      <div class="flex flex-col gap-2">
        <Skeleton class="h-8 w-2/3 max-w-md" />
        <Skeleton class="h-4 w-40" />
      </div>
      <Skeleton class="h-24 w-full max-w-xl" />
      <div class="flex flex-col gap-3">
        <Skeleton v-for="card in 3" :key="card" class="h-36 w-full" />
      </div>
    </template>
    <p v-else-if="notFound" class="text-body">{{ t('editorial.version.notFound') }}</p>
    <p v-else-if="isError || version === undefined" role="alert" class="text-body text-destructive">
      {{ t('editorial.version.loadFailed') }}
    </p>
    <template v-else>
      <header class="flex flex-col gap-2">
        <RouterLink
          :to="`/editorial/versions/${id}`"
          class="text-caption text-primary underline-offset-4 hover:underline"
        >
          {{ t('editorial.test.backToVersion') }}
        </RouterLink>
        <h1 class="text-title">{{ t('editorial.test.heading', { title: version.title }) }}</h1>
        <div class="text-caption text-muted-foreground flex flex-wrap items-center gap-3">
          <span class="tabular-nums">{{ t('editorial.version.number', { number: version.number }) }}</span>
          <span class="bg-secondary text-secondary-foreground rounded-md px-2 py-0.5">{{
            t(`glossary.courseVersionStatus.${version.status}`)
          }}</span>
        </div>
      </header>
      <p v-if="version.status !== 'Draft'" class="text-body bg-muted rounded-md p-3">
        {{ t(`editorial.version.readOnly.${version.status}`) }}
      </p>
      <TestBlueprintSection :version-id="id" :blueprint="version.blueprint" :editable="editable" />
      <section aria-labelledby="course-test-questions" class="flex flex-col gap-4">
        <h2 id="course-test-questions">{{ t('editorial.test.questions.title') }}</h2>
        <div v-if="counts !== undefined" class="text-body flex flex-col gap-1 tabular-nums">
          <p>
            {{
              t(
                'editorial.test.summary.reviewed',
                { reviewed: n(counts.approved, 'integer'), count: n(counts.required, 'integer') },
                counts.required,
              )
            }}
          </p>
          <p v-if="counts.held !== counts.required">
            {{
              t(
                'editorial.test.summary.held',
                { held: n(counts.held, 'integer'), count: n(counts.required, 'integer') },
                counts.required,
              )
            }}
          </p>
          <p v-if="counts.tooMany > 0">
            {{ t('editorial.test.summary.tooMany', { count: n(counts.tooMany, 'integer') }, counts.tooMany) }}
          </p>
          <p v-if="counts.unreviewed > 0">
            {{ t('editorial.test.summary.unreviewed', { count: n(counts.unreviewed, 'integer') }, counts.unreviewed) }}
          </p>
          <p v-else-if="counts.held > 0">{{ t('editorial.test.summary.allReviewed') }}</p>
        </div>
        <ol v-if="questions.length > 0" aria-labelledby="course-test-questions" class="flex flex-col gap-3">
          <QuestionCard
            v-for="question in questions"
            :key="question.id"
            :question="question"
            :chapter="chapters.get(question.chapterId)"
          />
        </ol>
        <EmptyState v-else>
          <template #icon><ListChecks /></template>
          {{ t('editorial.test.questions.empty') }}
          <template v-if="editable" #action>
            <Button v-if="version.chapters.length > 0" type="button" variant="outline">
              {{ t('editorial.test.questions.add') }}
            </Button>
            <p v-else class="text-body">{{ t('editorial.test.questions.noChapters') }}</p>
          </template>
        </EmptyState>
      </section>
    </template>
  </main>
</template>
