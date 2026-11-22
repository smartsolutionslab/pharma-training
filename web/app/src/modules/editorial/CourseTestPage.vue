<script setup lang="ts">
import { ListChecks } from '@lucide/vue';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRoute } from 'vue-router';
import { z } from 'zod';
import { useGetEditorialCourseVersionsId } from '@/api/generated';
import { ApiError } from '@/api/baseApi';
import { Button } from '@/components/ui/button';
import { Dialog, DialogClose, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { EmptyState } from '@/components/ui/empty-state';
import { Skeleton } from '@/components/ui/skeleton';
import type { EditorialQuestionDto, EditorialQuestionRequest } from '@/api/generated';
import { t as translate } from '@/i18n';
import { notify } from '@/lib/notify';
import QuestionCard from './QuestionCard.vue';
import QuestionForm from './QuestionForm.vue';
import RemoveQuestionDialog from './RemoveQuestionDialog.vue';
import TestBlueprintSection from './TestBlueprintSection.vue';
import { testCounts } from './testCounts';
import { useQuestionMutations } from './useQuestionMutations';

/** The most questions a test holds (Test.MaxQuestions). */
const maxQuestions = 50;

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

const { add, replace, remove, approve, isSaving, isRemoving, isApproving } = useQuestionMutations(id);
/** Whether a question may be approved: in a draft or in review (four eyes, ADR 0007). */
const approvable = computed(() => version.value?.status === 'Draft' || version.value?.status === 'InReview');
/** The question form while it is open: a question to replace, or none to add one. */
const form = ref<{ question?: EditorialQuestionDto }>();
/** The question asked about before it is deleted. */
const removing = ref<EditorialQuestionDto>();
/** Whether another question may be added: a draft with chapters and room for one. */
const canAdd = computed(
  () => editable.value && (version.value?.chapters.length ?? 0) > 0 && questions.value.length < maxQuestions,
);

/** Saves the form's question; a refusal is reported, and the form stays open with what was entered. */
async function save(request: EditorialQuestionRequest) {
  const question = form.value?.question;
  const result = question === undefined ? await add(request) : await replace(question.id, request);
  if (!result.ok) {
    notify.failure(result.serverMessage ?? translate('editorial.test.questions.saveFailed'));
    return false;
  }
  notify.success(translate('editorial.test.questions.saved'));
  form.value = undefined;
  return true;
}

async function approveQuestion(question: EditorialQuestionDto) {
  const position = Number(question.position);
  const result = await approve(question.id);
  if (result.ok) {
    notify.success(translate('editorial.test.questions.approved', { position }));
  } else {
    notify.failure(result.serverMessage ?? translate('editorial.test.questions.approveFailed', { position }));
  }
}

async function confirmRemove(question: EditorialQuestionDto) {
  const result = await remove(question.id);
  removing.value = undefined;
  if (result.ok) {
    notify.success(translate('editorial.test.questions.removed'));
  } else {
    notify.failure(result.serverMessage ?? translate('editorial.test.questions.removeFailed'));
  }
}
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
        <div class="flex flex-wrap items-center justify-between gap-2">
          <h2 id="course-test-questions">{{ t('editorial.test.questions.title') }}</h2>
          <Button v-if="canAdd && questions.length > 0" type="button" @click="form = {}">
            {{ t('editorial.test.questions.add') }}
          </Button>
        </div>
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
            :editable="editable"
            :approvable="approvable"
            :locked="isApproving"
            @edit="(each) => (form = { question: each })"
            @remove="(each) => (removing = each)"
            @approve="approveQuestion"
          />
        </ol>
        <EmptyState v-else>
          <template #icon><ListChecks /></template>
          {{ t('editorial.test.questions.empty') }}
          <template v-if="editable" #action>
            <Button v-if="version.chapters.length > 0" type="button" variant="outline" @click="form = {}">
              {{ t('editorial.test.questions.add') }}
            </Button>
            <p v-else class="text-body">{{ t('editorial.test.questions.noChapters') }}</p>
          </template>
        </EmptyState>
      </section>
      <template v-if="editable">
        <Dialog :open="form !== undefined" @update:open="(open) => !open && (form = undefined)">
          <DialogContent class="max-h-[90vh] overflow-y-auto sm:max-w-2xl" :aria-describedby="undefined">
            <DialogHeader>
              <DialogTitle>
                {{
                  t(form?.question ? 'editorial.test.questionForm.editTitle' : 'editorial.test.questionForm.addTitle')
                }}
              </DialogTitle>
            </DialogHeader>
            <QuestionForm
              v-if="form !== undefined"
              :chapters="version.chapters"
              :question="form.question"
              :on-save="save"
              :saving="isSaving"
            >
              <template #actions>
                <DialogClose as-child>
                  <Button type="button" variant="outline">{{ t('editorial.cancel') }}</Button>
                </DialogClose>
              </template>
            </QuestionForm>
          </DialogContent>
        </Dialog>
        <RemoveQuestionDialog
          :question="removing"
          :removing="isRemoving"
          @confirm="confirmRemove"
          @cancel="removing = undefined"
        />
      </template>
    </template>
  </main>
</template>
