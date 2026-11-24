<script setup lang="ts">
import { useQueryClient } from '@tanstack/vue-query';
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import { ApiError } from '@/api/baseApi';
import {
  type AssessmentAttemptQuestionDto,
  getGetAssessmentExaminationsBySessionSessionIdQueryKey,
  getGetLearningSessionsIdQueryKey,
  usePostAssessmentExaminationsIdAttempts,
  usePostAssessmentExaminationsIdAttemptsCurrentSubmit,
} from '@/api/generated';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { type AssessmentFailure, assessmentFailure, assessmentFailureText } from './assessmentErrors';
import ConfirmSubmitDialog from './ConfirmSubmitDialog.vue';
import { useAnswerSaver } from './useAnswerSaver';
import { useExamination } from './useExamination';

// The learner's test page (#48) at /assessment/sessions/:sessionId: every question of the attempt on one page, in the
// order the server sent them (ADR 0022), each answer saved at once. Before the start it says only which attempt of how
// many comes next; while the examination is not there yet it says so and asks again.
const { t } = useI18n();
const route = useRoute();
const router = useRouter();
const queryClient = useQueryClient();

const sessionId = computed(() => String(route.params.sessionId ?? ''));
const { examination, version, notYetThere, failureOf, retryable, loading, refetch } = useExamination(sessionId);
const failure = failureOf('read');
const examinationId = computed(() => examination.value?.id ?? '');
const examinationKey = computed(() => getGetAssessmentExaminationsBySessionSessionIdQueryKey(sessionId.value));
const resultAddress = computed(() => `/assessment/sessions/${sessionId.value}/result`);

/** What the polite status region says: the latest save only, each a message of its own so it is heard once. */
const announcement = ref<{ id: number; text: string }>();
let announced = 0;

const questions = computed(() => examination.value?.currentAttempt?.questions ?? []);
const numberOf = (questionId: string) => questions.value.findIndex((question) => question.id === questionId) + 1;

const saver = useAnswerSaver(sessionId, examinationId, {
  onSaved: (questionId) => {
    announced += 1;
    announcement.value = {
      id: announced,
      text: t('assessment.test.save.announced', { number: numberOf(questionId) }),
    };
  },
});

/** What the page shows as chosen: the draft on its way, else what the server holds. */
const shownChoice = (question: AssessmentAttemptQuestionDto) => saver.draftOf(question.id) ?? question.chosenOptionIds;
const answered = computed(() => questions.value.filter((question) => shownChoice(question).length > 0).length);

type PageState = 'notUnlocked' | 'unlocked' | 'inAttempt' | 'alreadySubmitted' | 'passed' | 'exhausted';
const state = computed((): PageState | undefined => {
  if (notYetThere.value) {
    return 'notUnlocked';
  }
  const read = examination.value;
  if (read === undefined) {
    return undefined;
  }
  if (read.status === 'Passed') {
    return 'passed';
  }
  if (read.status === 'Exhausted') {
    return 'exhausted';
  }
  if (saver.closed.value) {
    return 'alreadySubmitted';
  }
  if (read.status === 'Locked') {
    return 'notUnlocked';
  }
  return read.currentAttempt === null ? 'unlocked' : 'inAttempt';
});

const heading = useTemplateRef<HTMLElement>('heading');
const questionsHeading = useTemplateRef<HTMLElement>('questionsHeading');
/** Set by Retry and by the start: the heading named takes the focus once it shows. */
const focusWanted = ref<'heading' | 'questions'>();
watch(
  [focusWanted, heading, questionsHeading],
  async ([wanted, h1, h2]) => {
    const element = wanted === 'heading' ? h1 : wanted === 'questions' ? h2 : null;
    if (element === null) {
      return;
    }
    await nextTick();
    focusWanted.value = undefined;
    element.focus();
  },
  { flush: 'post' },
);

function retryLoading() {
  focusWanted.value = 'heading';
  void refetch();
}

const title = computed(() =>
  version.value === undefined
    ? t('assessment.test.headingFallback')
    : t('assessment.test.heading', { title: version.value.title }),
);
const intro = computed(() => {
  const read = examination.value;
  if (read === undefined || read.maxAttempts === null) {
    return undefined;
  }
  return t('assessment.test.intro', { number: Number(read.attemptsUsed) + 1, max: Number(read.maxAttempts) });
});

// The start: posted once, the examination read again, the focus on the questions.
const { mutateAsync: postStart, isPending: starting } = usePostAssessmentExaminationsIdAttempts();
const startFailure = ref<AssessmentFailure>();
const startRetryable = ref(true);
async function start() {
  if (starting.value || examinationId.value === '') {
    return;
  }
  startFailure.value = undefined;
  try {
    await postStart({ id: examinationId.value });
  } catch (error) {
    // Not unlocked: an attempt was opened elsewhere, or the test is locked; the examination read again says which.
    if (!(error instanceof ApiError && error.code === 'assessment.examination-not-unlocked')) {
      startFailure.value = assessmentFailure('start', error);
      startRetryable.value = !(error instanceof ApiError && error.code === 'assessment.identity-incomplete');
      return;
    }
  }
  focusWanted.value = 'questions';
  await queryClient.invalidateQueries({ queryKey: examinationKey.value });
}

function choose(question: AssessmentAttemptQuestionDto, optionId: string, event: Event) {
  if (question.type === 'SingleChoice') {
    saver.choose(question.id, [optionId]);
    return;
  }
  const input = event.target as HTMLInputElement;
  const chosen = new Set(shownChoice(question));
  if (input.checked) {
    chosen.add(optionId);
  } else {
    chosen.delete(optionId);
  }
  if (chosen.size === 0) {
    // An answer cannot be cleared: the earlier one stays saved, and stays shown.
    input.checked = true;
  }
  saver.choose(
    question.id,
    question.options.filter((option) => chosen.has(option.id)).map((option) => option.id),
  );
}

// The submit: confirmed, every answer saved first, posted once, then the result.
const confirming = ref(false);
const submitting = ref(false);
const unsaved = ref(false);
const submitFailure = ref<AssessmentFailure>();
const { mutateAsync: postSubmit } = usePostAssessmentExaminationsIdAttemptsCurrentSubmit();

async function submit() {
  if (submitting.value || examinationId.value === '') {
    return;
  }
  submitting.value = true;
  unsaved.value = false;
  submitFailure.value = undefined;
  try {
    if (!(await saver.settled())) {
      confirming.value = false;
      unsaved.value = true;
      return;
    }
    try {
      await postSubmit({ id: examinationId.value });
    } catch (error) {
      // Submitted elsewhere: the result is there all the same.
      if (!(error instanceof ApiError && error.code === 'assessment.no-open-attempt')) {
        confirming.value = false;
        submitFailure.value = assessmentFailure('submit', error);
        return;
      }
    }
    void queryClient.invalidateQueries({ queryKey: examinationKey.value });
    void queryClient.invalidateQueries({ queryKey: getGetLearningSessionsIdQueryKey(sessionId.value) });
    confirming.value = false;
    await router.push(resultAddress.value);
  } finally {
    submitting.value = false;
  }
}

const saveFailureText = (question: AssessmentAttemptQuestionDto) => {
  const saving = saver.stateOf(question.id);
  return saving.kind === 'failed' ? assessmentFailureText(saving.failure, { number: numberOf(question.id) }) : '';
};

const headingId = 'examination-title';
const questionsHeadingId = 'examination-questions';
const inputId = (question: AssessmentAttemptQuestionDto, optionId: string) => `answer-${question.id}-${optionId}`;
</script>

<template>
  <div class="flex flex-col gap-6" :aria-busy="loading ? 'true' : undefined">
    <div role="status" aria-live="polite" class="sr-only">
      <p v-if="announcement !== undefined" :key="announcement.id">{{ announcement.text }}</p>
    </div>

    <div v-if="failure !== undefined" role="alert" class="flex flex-col items-start gap-3">
      <p class="text-body">{{ assessmentFailureText(failure) }}</p>
      <Button v-if="retryable" variant="outline" @click="retryLoading">{{ t('shell.retry') }}</Button>
    </div>

    <template v-else-if="loading">
      <Skeleton class="h-8 w-2/3" />
      <Skeleton v-for="card in 3" :key="card" class="h-32 w-full" />
    </template>

    <template v-else-if="state !== undefined">
      <h1 :id="headingId" ref="heading" tabindex="-1" class="text-title focus:outline-none">{{ title }}</h1>

      <section v-if="state === 'notUnlocked'" class="flex flex-col items-start gap-3">
        <p class="text-body">{{ t('assessment.test.notUnlocked') }}</p>
        <RouterLink to="/learning" class="text-body text-primary underline underline-offset-4">
          {{ t('assessment.test.toCourses') }}
        </RouterLink>
      </section>

      <section
        v-else-if="state === 'passed' || state === 'exhausted' || state === 'alreadySubmitted'"
        class="flex flex-col items-start gap-3"
      >
        <p class="text-body">{{ t(`assessment.test.${state}`) }}</p>
        <RouterLink :to="resultAddress" class="text-body text-primary underline underline-offset-4">
          {{ t('assessment.test.toResult') }}
        </RouterLink>
      </section>

      <section v-else-if="state === 'unlocked'" class="flex flex-col items-start gap-3">
        <p v-if="intro !== undefined" class="text-body tabular-nums">{{ intro }}</p>
        <p v-else class="text-body">{{ t('assessment.test.notReady') }}</p>
        <RouterLink
          v-if="examination?.lastResult"
          :to="resultAddress"
          class="text-body text-primary underline underline-offset-4"
        >
          {{ t('assessment.test.toResult') }}
        </RouterLink>
        <div v-if="startFailure !== undefined" role="alert" class="flex flex-col items-start gap-3">
          <p class="text-body">{{ assessmentFailureText(startFailure) }}</p>
          <Button v-if="startRetryable" variant="outline" :disabled="starting" @click="start">
            {{ t('shell.retry') }}
          </Button>
        </div>
        <Button :disabled="intro === undefined || starting" @click="start">{{ t('assessment.test.start') }}</Button>
      </section>

      <section v-else :aria-labelledby="questionsHeadingId" class="flex flex-col gap-6">
        <h2
          :id="questionsHeadingId"
          ref="questionsHeading"
          tabindex="-1"
          class="text-body font-semibold focus:outline-none"
        >
          {{ t('assessment.test.questions') }}
        </h2>
        <p class="text-body text-muted-foreground tabular-nums">
          {{ t('assessment.test.answered', { answered, total: questions.length }) }}
        </p>

        <fieldset
          v-for="(question, index) in questions"
          :key="question.id"
          class="bg-card shadow-elevation-1 flex flex-col gap-3 rounded-md border p-4"
        >
          <legend class="text-body float-left mb-1 w-full font-semibold">
            <span class="block">{{
              t('assessment.test.question', { number: index + 1, total: questions.length })
            }}</span>
            <span class="block">{{ question.text }}</span>
          </legend>
          <p class="text-caption text-muted-foreground">
            {{ t(question.type === 'SingleChoice' ? 'assessment.test.singleHint' : 'assessment.test.multipleHint') }}
          </p>
          <div class="flex flex-col gap-2">
            <label
              v-for="option in question.options"
              :key="option.id"
              :for="inputId(question, option.id)"
              class="text-body flex items-center gap-2"
            >
              <input
                :id="inputId(question, option.id)"
                :type="question.type === 'SingleChoice' ? 'radio' : 'checkbox'"
                :name="`question-${question.id}`"
                :value="option.id"
                :checked="shownChoice(question).includes(option.id)"
                :disabled="submitting"
                class="accent-primary size-4"
                @change="(event) => choose(question, option.id, event)"
              />
              <span>{{ option.text }}</span>
            </label>
          </div>
          <div v-if="saver.stateOf(question.id).kind === 'failed'" role="alert" class="flex flex-col items-start gap-2">
            <p class="text-body">{{ saveFailureText(question) }}</p>
            <Button variant="outline" size="sm" @click="saver.retry(question.id)">
              {{ t('assessment.test.save.retry') }}
            </Button>
          </div>
          <p v-else-if="saver.stateOf(question.id).kind === 'keepOne'" class="text-caption">
            {{ t('assessment.test.save.keepOne') }}
          </p>
          <p v-else-if="saver.stateOf(question.id).kind === 'saving'" class="text-caption text-muted-foreground">
            {{ t('assessment.test.save.saving') }}
          </p>
          <p v-else-if="saver.stateOf(question.id).kind === 'saved'" class="text-caption text-muted-foreground">
            {{ t('assessment.test.save.saved') }}
          </p>
        </fieldset>

        <div v-if="unsaved" role="alert" class="text-body">{{ t('assessment.test.unsaved') }}</div>
        <div v-if="submitFailure !== undefined" role="alert" class="flex flex-col items-start gap-3">
          <p class="text-body">{{ assessmentFailureText(submitFailure) }}</p>
          <Button variant="outline" :disabled="submitting" @click="submit">{{ t('shell.retry') }}</Button>
        </div>

        <Button class="self-start" :disabled="submitting" @click="confirming = true">
          {{ t('assessment.test.submit') }}
        </Button>
        <ConfirmSubmitDialog
          :open="confirming"
          :answered="answered"
          :total="questions.length"
          :pending="submitting"
          @confirm="submit"
          @cancel="confirming = false"
        />
      </section>
    </template>
  </div>
</template>
