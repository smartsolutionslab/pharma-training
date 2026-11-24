<script setup lang="ts">
import { ListChecks, LoaderCircle, Sparkles, X } from '@lucide/vue';
import { computed, nextTick, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRoute } from 'vue-router';
import { z } from 'zod';
import { useIsMutating } from '@tanstack/vue-query';
import { useGetEditorialCourseVersionsId } from '@/api/generated';
import { ApiError } from '@/api/baseApi';
import { Button } from '@/components/ui/button';
import { Dialog, DialogClose, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { EmptyState } from '@/components/ui/empty-state';
import { Skeleton } from '@/components/ui/skeleton';
import type { EditorialChapterDto, EditorialQuestionDto, EditorialQuestionRequest } from '@/api/generated';
import { type MessageKey, t as translate } from '@/i18n';
import { errorText } from '@/i18n/errorText';
import { notify } from '@/lib/notify';
import QuestionCard from './QuestionCard.vue';
import QuestionForm from './QuestionForm.vue';
import RemoveQuestionDialog from './RemoveQuestionDialog.vue';
import TestBlueprintSection from './TestBlueprintSection.vue';
import TestDraftDialog from './TestDraftDialog.vue';
import { testCounts } from './testCounts';
import { draftFailure } from './testDraftErrors';
import { useQuestionMutations } from './useQuestionMutations';
import { useTestDraft } from './useTestDraft';

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
    notify.failure(errorText(result.error, 'editorial.test.questions.saveFailed'));
    return false;
  }
  notify.success(translate('editorial.test.questions.saved'));
  form.value = undefined;
  return true;
}

const { draft, isDrafting } = useTestDraft(id);
/** Whether nothing else may change for now: a change while the AI drafts makes the draft's save lose the race. */
const locked = computed(() => isDrafting.value || isApproving.value);
/** How many changes to this version run, the draft, a question's or the blueprint's: a draft waits for all of them. */
const changing = useIsMutating(() => {
  const versionId = id.value;
  return { predicate: (mutation) => (mutation.state.variables as { id?: unknown } | undefined)?.id === versionId };
});
const busy = computed(() => changing.value > 0 || isDrafting.value || isSaving.value || isRemoving.value);
/** Whether the AI draft is being confirmed. */
const confirmingDraft = ref(false);
/** The chapters the last draft left out and those it cut, by id, until the note is dismissed; never stored. */
const leftOutIds = ref<string[]>([]);
const truncatedIds = ref<string[]>([]);
/** The titles of the chapters by id, in the draft's order: an id the version does not have is skipped. */
const titlesOf = (ids: string[]) =>
  ids.flatMap((chapterId) => {
    const chapter = chapters.value.get(chapterId);
    return chapter === undefined ? [] : [chapter.title];
  });
const leftOutTitles = computed(() => titlesOf(leftOutIds.value));
const truncatedTitles = computed(() => titlesOf(truncatedIds.value));
/**
 * Whether the AI drafts from a chapter, as the host decides (TestDraftRequest, #157): a text chapter's Markdown, or a
 * PDF chapter's text, extracted whole or cut at a limit. Videos and PDFs without text are left out.
 */
const hasText = (chapter: EditorialChapterDto) =>
  (chapter.kind === 'Text' && chapter.text !== null) ||
  (chapter.kind === 'Pdf' && (chapter.pdfText === 'Complete' || chapter.pdfText === 'Truncated'));
/** Why the host would refuse a draft, when the page can tell without asking the model. */
const draftRefusal = computed((): MessageKey | undefined => {
  if (!(version.value?.chapters ?? []).some(hasText)) {
    return 'editorial.test.draft.noTextChapters';
  }
  return counts.value?.toDraft === 0 ? 'editorial.test.draft.nothingToDraft' : undefined;
});
/** Whether the draft leaves chapters out for want of text: a video, a PDF without text. */
const hasChaptersWithoutText = computed(() => (version.value?.chapters ?? []).some((chapter) => !hasText(chapter)));
function dismissDraftNote() {
  leftOutIds.value = [];
  truncatedIds.value = [];
}
/** The note in brief, for the draft's polite status: those who do not see the note hear what it names. */
const draftSummary = computed(() =>
  [
    ...(leftOutTitles.value.length > 0
      ? [
          t(
            'editorial.test.draft.leftOutSummary',
            { count: n(leftOutTitles.value.length, 'integer') },
            leftOutTitles.value.length,
          ),
        ]
      : []),
    ...(truncatedTitles.value.length > 0
      ? [
          t(
            'editorial.test.draft.truncatedSummary',
            { count: n(truncatedTitles.value.length, 'integer') },
            truncatedTitles.value.length,
          ),
        ]
      : []),
  ].join(' '),
);

// Where focus goes after an action (S4): the button pressed may be gone once the version is read again, so focus goes
// where the editor goes on, never to the body.
const draftStatus = 'course-test-draft-status';
const questionsHeading = 'course-test-questions';
const cardOf = (questionId: string) => `question-${questionId}`;
const approveButtonOf = (questionId: string) => `question-${questionId}-approve`;
/** The element a closing dialog gives focus to, instead of the button that opened it. */
let focusOnClose: string | undefined;
/** The element to focus once it can take focus (a button the page still disables). */
const focusWhenReady = ref<string>();

function dialogClosed(event: Event) {
  const target = focusOnClose === undefined ? null : document.getElementById(focusOnClose);
  focusOnClose = undefined;
  if (target !== null) {
    event.preventDefault();
    target.focus();
  }
}

function focusPending() {
  if (focusWhenReady.value === undefined) {
    return;
  }
  const target = document.getElementById(focusWhenReady.value);
  if (target === null) {
    focusWhenReady.value = undefined;
  } else if (!(target instanceof HTMLButtonElement && target.disabled)) {
    focusWhenReady.value = undefined;
    target.focus();
  }
}
watch([focusWhenReady, questions, locked], focusPending, { flush: 'post' });

/** Dismisses the draft's note; its button goes with it, so focus goes to the draft's status. */
async function dismissNote() {
  dismissDraftNote();
  await nextTick();
  document.getElementById(draftStatus)?.focus();
}

/** Asks for the draft once confirmed; it cannot be cancelled, since the model's call would go on. */
async function startDraft() {
  if (busy.value) {
    return;
  }
  focusOnClose = draftStatus;
  confirmingDraft.value = false;
  dismissDraftNote();
  const outcome = await draft();
  switch (outcome.status) {
    case 'drafted':
      notify.success(translate('editorial.test.draft.done', { count: n(outcome.drafted, 'integer') }, outcome.drafted));
      leftOutIds.value = outcome.leftOutChapterIds;
      truncatedIds.value = outcome.truncatedChapterIds;
      break;
    case 'unreadable':
      // The host's answer is not the draft's shape: nothing of it is shown, whether it was applied is not known, and
      // the version is read again.
      notify.failure(translate('editorial.test.draft.errors.unknown'));
      break;
    case 'failed':
      notify.failure(draftFailure(outcome.error));
      break;
  }
}

async function approveQuestion(question: EditorialQuestionDto) {
  const position = Number(question.position);
  const result = await approve(question.id);
  // The version is read again by now: on to the next unreviewed question, else to this question's card.
  const unreviewed = approvable.value ? questions.value.filter((each) => each.review === 'Unreviewed') : [];
  const next = unreviewed.find((each) => Number(each.position) >= position) ?? unreviewed[0];
  const approved = questions.value.some((each) => each.id === question.id);
  focusWhenReady.value =
    next !== undefined ? approveButtonOf(next.id) : approved ? cardOf(question.id) : questionsHeading;
  await nextTick();
  focusPending();
  if (result.ok) {
    notify.success(translate('editorial.test.questions.approved', { position }));
  } else {
    notify.failure(errorText(result.error, 'editorial.test.questions.approveFailed', { values: { position } }));
  }
}

async function confirmRemove(question: EditorialQuestionDto) {
  const following = questions.value[questions.value.findIndex((each) => each.id === question.id) + 1]?.id;
  const result = await remove(question.id);
  if (result.ok) {
    // The version is read again by now: on to the question that followed, else to the questions' heading.
    focusOnClose =
      following !== undefined && questions.value.some((each) => each.id === following)
        ? cardOf(following)
        : questionsHeading;
  }
  removing.value = undefined;
  if (result.ok) {
    notify.success(translate('editorial.test.questions.removed'));
  } else {
    notify.failure(errorText(result.error, 'editorial.test.questions.removeFailed'));
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
      <p v-if="version.status === 'InReview'" class="text-body bg-muted rounded-md p-3">
        {{ t('editorial.test.readOnly.InReviewApprove') }}
      </p>
      <TestBlueprintSection :version-id="id" :blueprint="version.blueprint" :editable="editable" :locked="locked">
        <div v-if="editable" class="flex flex-col gap-3 border-t pt-4">
          <p class="text-body text-muted-foreground">{{ t('editorial.test.draft.explain') }}</p>
          <Button
            type="button"
            class="self-start"
            :disabled="busy || draftRefusal !== undefined"
            :aria-describedby="draftRefusal === undefined ? undefined : 'course-test-draft-refusal'"
            @click="confirmingDraft = true"
          >
            <LoaderCircle v-if="isDrafting" class="motion-safe:animate-spin" aria-hidden="true" />
            <Sparkles v-else aria-hidden="true" />
            {{ t('editorial.test.draft.start') }}
          </Button>
          <p
            v-if="draftRefusal !== undefined"
            id="course-test-draft-refusal"
            class="text-caption text-muted-foreground"
          >
            {{ t(draftRefusal) }}
          </p>
          <p :id="draftStatus" role="status" tabindex="-1" class="text-body">
            <template v-if="isDrafting">{{ t('editorial.test.draft.running') }}</template>
            <!-- The note below shows the same to the eye; the summary is for those who hear the status. -->
            <span v-else class="sr-only">{{ draftSummary }}</span>
          </p>
          <div
            v-if="leftOutTitles.length > 0 || truncatedTitles.length > 0"
            class="bg-muted flex items-start justify-between gap-2 rounded-md p-3"
          >
            <div class="text-body flex flex-col gap-3">
              <div v-if="leftOutTitles.length > 0" class="flex flex-col gap-2">
                <p>{{ t('editorial.test.draft.leftOut') }}</p>
                <ul class="list-disc pl-5">
                  <li v-for="(title, index) in leftOutTitles" :key="index">{{ title }}</li>
                </ul>
              </div>
              <div v-if="truncatedTitles.length > 0" class="flex flex-col gap-2">
                <p>{{ t('editorial.test.draft.truncated') }}</p>
                <ul class="list-disc pl-5">
                  <li v-for="(title, index) in truncatedTitles" :key="index">{{ title }}</li>
                </ul>
              </div>
            </div>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              :aria-label="t('editorial.test.draft.dismiss')"
              @click="dismissNote"
            >
              <X aria-hidden="true" />
            </Button>
          </div>
        </div>
      </TestBlueprintSection>
      <section aria-labelledby="course-test-questions" class="flex flex-col gap-4">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <h2 :id="questionsHeading" tabindex="-1">{{ t('editorial.test.questions.title') }}</h2>
          <Button v-if="canAdd && questions.length > 0" type="button" :disabled="locked" @click="form = {}">
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
            :locked="locked"
            @edit="(each) => (form = { question: each })"
            @remove="(each) => (removing = each)"
            @approve="approveQuestion"
          />
        </ol>
        <EmptyState v-else>
          <template #icon><ListChecks /></template>
          {{ t('editorial.test.questions.empty') }}
          <template v-if="editable" #action>
            <Button
              v-if="version.chapters.length > 0"
              type="button"
              variant="outline"
              :disabled="locked"
              @click="form = {}"
            >
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
        <TestDraftDialog
          :open="confirmingDraft"
          :to-draft="counts?.toDraft ?? 0"
          :unreviewed="counts?.unreviewed ?? 0"
          :has-chapters-without-text="hasChaptersWithoutText"
          @confirm="startDraft"
          @cancel="confirmingDraft = false"
          @closed="dialogClosed"
        />
        <RemoveQuestionDialog
          :question="removing"
          :removing="isRemoving"
          @confirm="confirmRemove"
          @cancel="removing = undefined"
          @closed="dialogClosed"
        />
      </template>
    </template>
  </main>
</template>
