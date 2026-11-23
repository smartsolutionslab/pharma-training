<script setup lang="ts">
import { BookOpen } from '@lucide/vue';
import { computed, nextTick, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import { z } from 'zod';
import {
  type EditorialChapterDto,
  type EditorialChapterRequest,
  useGetEditorialCourseVersionsId,
} from '@/api/generated';
import { ApiError } from '@/api/baseApi';
import { Button } from '@/components/ui/button';
import { Dialog, DialogClose, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { EmptyState } from '@/components/ui/empty-state';
import { Skeleton } from '@/components/ui/skeleton';
import { t as translate } from '@/i18n';
import { notify } from '@/lib/notify';
import ChapterForm from './ChapterForm.vue';
import ChapterList from './ChapterList.vue';
import ConfirmWorkflowDialog from './ConfirmWorkflowDialog.vue';
import { inboxStatuses } from './inboxQuery';
import { publishChecklist, type PublishRefusal } from './publishChecklist';
import PublishChecklist from './PublishChecklist.vue';
import RemoveChapterDialog from './RemoveChapterDialog.vue';
import ReturnVersionDialog from './ReturnVersionDialog.vue';
import { useChapterMutations } from './useChapterMutations';
import { useCourseVersionWorkflow, type WorkflowResult } from './useCourseVersionWorkflow';
import { useDraftNextVersion } from './useDraftNextVersion';
import { useReorderChapters } from './useReorderChapters';
import { type WorkflowAction, type WorkflowFailure, workflowFailure, workflowFailureText } from './workflowErrors';

// A course version (/editorial/versions/:id): its title, number and status, the way back to its inbox tab, and its
// chapters. Only a draft can be changed; any other status says why not and shows its chapters read-only.
const route = useRoute();
const router = useRouter();
const id = computed(() => String(route.params.id ?? ''));
/** The id goes into the requests' paths: one that is no UUID names no version, and is never sent. */
const validId = computed(() => z.uuid().safeParse(id.value).success);
const { data: version, isPending, error } = useGetEditorialCourseVersionsId(id, { query: { enabled: validId } });
const { add, replace, remove, isSaving } = useChapterMutations(id);
const { reorder } = useReorderChapters(id);
const { draftNext } = useDraftNextVersion(id);
const workflow = useCourseVersionWorkflow(id);
const { t } = useI18n();

/** The chapter form while it is open: with the chapter it replaces, or none to add one. */
const form = ref<{ chapter?: EditorialChapterDto }>();
/** The chapter whose deletion is being asked about. */
const removing = ref<EditorialChapterDto>();
const isRemoving = ref(false);
/** Whether the chapter form's file is on its way to storage: the dialog stays open until it is there or cancelled. */
const chapterUploading = ref(false);

/** Set from the first press until the host has answered, so a second press drafts nothing. */
const draftingNext = ref(false);
/** The drafted version whose heading takes the focus once it shows. */
const focusHeadingOf = ref<string>();
const headingId = 'course-version-title';
const newVersionId = 'course-version-new-version';

/** The workflow's step from its press until the host has answered and the version is read again. */
const stepping = ref<WorkflowAction>();
/** The step a confirmation is asked for. */
const confirming = ref<'publish' | 'retire'>();
const returning = ref(false);
/** Why four eyes refused this page's publish: it holds until the version's id or status changes. */
const fourEyesRefusal = ref<PublishRefusal>();
/** The elements to focus, the first one there, once it can take focus and no dialog is open or closing. */
const focusWhenReady = ref<string[]>();
/** Whether a step's dialog has been told to close and has not yet given the focus back. */
let dialogClosing = false;
/** How many of the steps' dialogs are on the page: none while it shows no version. */
let dialogsMounted = 0;
const stepButtonId = (action: WorkflowAction) => `course-version-${action}`;

/** The steps each status offers. */
const steps = computed((): WorkflowAction[] => {
  switch (version.value?.status) {
    case 'Draft':
      return ['submit'];
    case 'InReview':
      return ['publish', 'return'];
    case 'Published':
    case 'Superseded':
      return ['retire'];
    default:
      return [];
  }
});
const checklist = computed(() => {
  const shown = version.value;
  if (shown === undefined || (shown.status !== 'Draft' && shown.status !== 'InReview')) {
    return undefined;
  }
  return publishChecklist(shown, fourEyesRefusal.value);
});

// A refusal says why this version could not be published as it was: returned, submitted again or another one, it no
// longer does.
watch([() => version.value?.id, () => version.value?.status], () => {
  fourEyesRefusal.value = undefined;
});

const notFound = computed(() => error.value instanceof ApiError && error.value.status === 404);
const editable = computed(() => version.value?.status === 'Draft');
/** The inbox tab the version is listed in; a Superseded or Retired one is in none, so the inbox's default. */
const inbox = computed(() => {
  const status = version.value?.status;
  return inboxStatuses.some((each) => each === status) ? `/editorial?status=${status}` : '/editorial';
});

/** Saves the form's chapter; a refusal is reported, and the dialog stays open with what was entered. */
async function save(request: EditorialChapterRequest) {
  const chapter = form.value?.chapter;
  const result = chapter === undefined ? await add(request) : await replace(chapter.id, request);
  if (!result.ok) {
    notify.failure(result.serverMessage ?? translate('editorial.chapters.saveFailed', { title: request.title }));
    return false;
  }
  notify.success(translate('editorial.chapters.saved', { title: request.title }));
  form.value = undefined;
  return true;
}

/**
 * Drafts the published version's successor and opens it, unless the editor has left meanwhile; a refusal is reported
 * and the page stays, the version read again, with the focus on the button or, when the version no longer offers it,
 * on its heading. The button stays shut from the press until the page has moved on or the host has refused.
 */
async function draftNextVersion() {
  const published = version.value;
  if (draftingNext.value || stepping.value !== undefined || published === undefined) {
    return;
  }
  draftingNext.value = true;
  const source = router.currentRoute.value.path;
  const result = await draftNext();
  if (result.ok) {
    const reported = () =>
      notify.success(translate('editorial.version.newVersionCreated', { number: Number(published.number) + 1 }));
    if (router.currentRoute.value.path !== source) {
      draftingNext.value = false;
      reported();
      return;
    }
    focusHeadingOf.value = result.id;
    await router.push(`/editorial/versions/${result.id}`);
    draftingNext.value = false;
    reported();
    return;
  }
  draftingNext.value = false;
  notify.failure(result.serverMessage ?? translate('editorial.version.newVersionFailed'));
  await nextTick();
  (document.getElementById(newVersionId) ?? document.getElementById(headingId))?.focus();
}

// The drafted version's heading takes the focus once it shows: the button that had it is gone.
watch(
  () => version.value?.id,
  async (shown) => {
    if (shown === undefined || shown !== focusHeadingOf.value) {
      return;
    }
    await nextTick();
    const heading = document.getElementById(headingId);
    if (heading !== null) {
      focusHeadingOf.value = undefined;
      heading.focus();
    }
  },
  { flush: 'post' },
);

/** What each step reports once taken. */
const takenKeys = {
  submit: 'editorial.workflow.submitted',
  publish: 'editorial.workflow.published',
  return: 'editorial.workflow.returned',
  retire: 'editorial.workflow.retired',
} as const;

function focusPending() {
  const candidates = focusWhenReady.value;
  if (candidates === undefined || dialogClosing || confirming.value !== undefined || returning.value) {
    return;
  }
  const target = candidates.map((each) => document.getElementById(each)).find((each) => each !== null);
  if (target === undefined || target === null) {
    focusWhenReady.value = undefined;
  } else if (!(target instanceof HTMLButtonElement && target.disabled)) {
    focusWhenReady.value = undefined;
    target.focus();
  }
}
watch([focusWhenReady, version, stepping, confirming, returning], focusPending, { flush: 'post' });

/** A dialog gives the focus to where the step left it, never back to the button that opened it unasked. */
function dialogClosed(event: Event) {
  event.preventDefault();
  dialogClosing = false;
  focusPending();
}

/** A dialog taken off the page while it closed never says it closed: the focus is free to move. */
function dialogMounted() {
  dialogsMounted += 1;
}
function dialogUnmounted() {
  dialogsMounted -= 1;
  dialogClosing = false;
  focusPending();
}

/** Closes the step's dialog; the focus moves once it has closed, or at once when no dialog is on the page. */
function closeDialogs() {
  if ((confirming.value !== undefined || returning.value) && dialogsMounted > 0) {
    dialogClosing = true;
  }
  confirming.value = undefined;
  returning.value = false;
}

/**
 * Takes a step and reports it: the version has been read again by now where the refusal may have changed it. Taken, the
 * focus goes to the heading; refused, to the button pressed while the version still offers it, else to the heading. A
 * refused publish by four eyes adds why to the checklist. A refusal the dialog keeps open for is answered unreported:
 * the dialog's caller says what follows.
 */
async function take(
  action: WorkflowAction,
  step: () => Promise<WorkflowResult>,
  keepOpen?: (failure: WorkflowFailure) => boolean,
): Promise<'done' | WorkflowFailure> {
  const before = version.value;
  if (stepping.value !== undefined || draftingNext.value || before === undefined) {
    return 'done';
  }
  stepping.value = action;
  const result = await step();
  stepping.value = undefined;
  if (result.ok) {
    closeDialogs();
    focusWhenReady.value = [headingId];
    notify.success(translate(takenKeys[action], { number: before.number }));
    return 'done';
  }
  const failure = workflowFailure(action, result.error);
  if (keepOpen?.(failure) === true) {
    return failure;
  }
  const key = 'key' in failure ? failure.key : undefined;
  if (key === 'editorial.workflow.errors.fourEyes' || key === 'editorial.workflow.errors.publisherApprovedQuestions') {
    fourEyesRefusal.value = key === 'editorial.workflow.errors.fourEyes' ? 'fourEyes' : 'publisherApproved';
  }
  closeDialogs();
  focusWhenReady.value = [stepButtonId(action), headingId];
  notify.failure(workflowFailureText(failure));
  return 'done';
}

function startStep(action: WorkflowAction) {
  switch (action) {
    case 'submit':
      void take('submit', workflow.submit);
      break;
    case 'publish':
    case 'retire':
      confirming.value = action;
      break;
    case 'return':
      returning.value = true;
      break;
  }
}

function confirmStep(action: 'publish' | 'retire') {
  void take(action, action === 'publish' ? workflow.publish : workflow.retire);
}

function cancelDialog() {
  const action = confirming.value ?? (returning.value ? 'return' : undefined);
  closeDialogs();
  if (action !== undefined) {
    focusWhenReady.value = [stepButtonId(action), headingId];
  }
}

const isKey = (failure: WorkflowFailure, key: string) => 'key' in failure && failure.key === key;

/**
 * Returns the version with the reason. The dialog stays open with the reason when the host refused the reason itself
 * (the field says why), or when the version changed meanwhile and, read again, is still in review: the toast says so,
 * and the editor sends the reason again.
 */
async function returnVersion(reason: string): Promise<'done' | 'reasonRefused' | 'tryAgain'> {
  const outcome = await take(
    'return',
    () => workflow.returnToDraft(reason),
    (failure) =>
      isKey(failure, 'editorial.workflow.returnForm.reasonRefused') ||
      (isKey(failure, 'editorial.workflow.errors.changed') && version.value?.status === 'InReview'),
  );
  if (outcome === 'done') {
    return 'done';
  }
  if (isKey(outcome, 'editorial.workflow.returnForm.reasonRefused')) {
    return 'reasonRefused';
  }
  notify.failure(workflowFailureText(outcome));
  return 'tryAgain';
}

/** Deletes the chapter asked about, and reports how that went. */
async function confirmRemove(chapter: EditorialChapterDto) {
  isRemoving.value = true;
  const result = await remove(chapter.id);
  isRemoving.value = false;
  removing.value = undefined;
  if (result.ok) {
    notify.success(translate('editorial.chapters.removed', { title: chapter.title }));
  } else {
    notify.failure(result.serverMessage ?? translate('editorial.chapters.removeFailed', { title: chapter.title }));
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
      <div class="flex flex-col gap-2">
        <Skeleton v-for="row in 3" :key="row" class="h-14 w-full" />
      </div>
    </template>
    <p v-else-if="notFound" class="text-body">{{ t('editorial.version.notFound') }}</p>
    <p v-else-if="version === undefined" role="alert" class="text-body text-destructive">
      {{ t('editorial.version.loadFailed') }}
    </p>
    <template v-else>
      <header class="flex flex-col gap-2">
        <RouterLink :to="inbox" class="text-caption text-primary underline-offset-4 hover:underline">
          {{ t('editorial.version.backToInbox') }}
        </RouterLink>
        <h1 :id="headingId" tabindex="-1" class="text-title outline-none">{{ version.title }}</h1>
        <div class="text-caption text-muted-foreground flex flex-wrap items-center gap-3">
          <span class="tabular-nums">{{ t('editorial.version.number', { number: version.number }) }}</span>
          <span class="bg-secondary text-secondary-foreground rounded-md px-2 py-0.5">{{
            t(`glossary.courseVersionStatus.${version.status}`)
          }}</span>
        </div>
        <RouterLink
          :to="`/editorial/versions/${id}/test`"
          class="text-body text-primary self-start underline-offset-4 hover:underline"
        >
          {{ t('editorial.version.openTest') }}
        </RouterLink>
      </header>
      <p v-if="version.status !== 'Draft'" class="text-body bg-muted rounded-md p-3">
        {{ t(`editorial.version.readOnly.${version.status}`) }}
      </p>
      <div v-if="version.status === 'Published'">
        <Button
          :id="newVersionId"
          type="button"
          :disabled="draftingNext || stepping !== undefined"
          @click="draftNextVersion"
        >
          {{ t('editorial.version.newVersion') }}
        </Button>
      </div>
      <section
        v-if="steps.length > 0"
        aria-labelledby="course-version-workflow"
        class="flex flex-col gap-4 rounded-md border p-4"
      >
        <h2 id="course-version-workflow">{{ t('editorial.workflow.title') }}</h2>
        <PublishChecklist v-if="checklist !== undefined" :items="checklist" />
        <div class="flex flex-wrap gap-2">
          <Button
            v-for="action in steps"
            :id="stepButtonId(action)"
            :key="action"
            type="button"
            :variant="action === 'return' || action === 'retire' ? 'outline' : 'default'"
            :disabled="stepping !== undefined || draftingNext"
            @click="startStep(action)"
          >
            {{ t(`editorial.workflow.${action}`) }}
          </Button>
        </div>
      </section>
      <section aria-labelledby="course-version-chapters" class="flex flex-col gap-4">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <h2 id="course-version-chapters">{{ t('editorial.chapters.title') }}</h2>
          <Button v-if="editable && version.chapters.length > 0" type="button" @click="form = {}">
            {{ t('editorial.chapters.add') }}
          </Button>
        </div>
        <ChapterList
          v-if="version.chapters.length > 0"
          :chapters="version.chapters"
          :editable="editable"
          @edit="(chapter) => (form = { chapter })"
          @remove="(chapter) => (removing = chapter)"
          @reorder="reorder"
        />
        <EmptyState v-else>
          <template #icon><BookOpen /></template>
          {{ t('editorial.chapters.empty') }}
          <template v-if="editable" #action>
            <Button type="button" variant="outline" @click="form = {}">{{ t('editorial.chapters.add') }}</Button>
          </template>
        </EmptyState>
      </section>
      <ConfirmWorkflowDialog
        :step="confirming"
        :number="Number(version.number)"
        :title="version.title"
        :pending="stepping !== undefined"
        @confirm="confirmStep"
        @cancel="cancelDialog"
        @closed="dialogClosed"
        @vue:mounted="dialogMounted"
        @vue:unmounted="dialogUnmounted"
      />
      <ReturnVersionDialog
        :open="returning"
        :pending="stepping !== undefined"
        :on-return="returnVersion"
        @cancel="cancelDialog"
        @closed="dialogClosed"
        @vue:mounted="dialogMounted"
        @vue:unmounted="dialogUnmounted"
      />
      <template v-if="editable">
        <Dialog :open="form !== undefined" @update:open="(open) => !open && !chapterUploading && (form = undefined)">
          <DialogContent class="sm:max-w-2xl" :aria-describedby="undefined" :close-disabled="chapterUploading">
            <DialogHeader>
              <DialogTitle>
                {{ t(form?.chapter ? 'editorial.chapterForm.editTitle' : 'editorial.chapterForm.addTitle') }}
              </DialogTitle>
            </DialogHeader>
            <ChapterForm
              v-if="form !== undefined"
              :version-id="id"
              :chapter="form.chapter"
              :questions="version.questions"
              :on-save="save"
              :saving="isSaving"
              @busy="(busy) => (chapterUploading = busy)"
            >
              <template #actions="{ busy }">
                <DialogClose as-child>
                  <Button type="button" variant="outline" :disabled="busy">{{ t('editorial.cancel') }}</Button>
                </DialogClose>
              </template>
            </ChapterForm>
          </DialogContent>
        </Dialog>
        <RemoveChapterDialog
          :chapter="removing"
          :questions="version.questions"
          :removing="isRemoving"
          @confirm="confirmRemove"
          @cancel="removing = undefined"
        />
      </template>
    </template>
  </main>
</template>
