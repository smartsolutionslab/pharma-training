<script setup lang="ts">
import { useForm } from 'vee-validate';
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import type { CatalogCourseDto, ComplianceMemberDto, ComplianceRefusedLearnerDto } from '@/api/generated';
import { Button } from '@/components/ui/button';
import { toTypedSchema } from '@/forms/toTypedSchema';
import { type MessageKey, t as translate } from '@/i18n';
import { calendarDate } from '@/i18n/formats';
import { notify } from '@/lib/notify';
import { useCurrentPharmacy } from '@/shell/useCurrentPharmacy';
import AssignCourseStep from './AssignCourseStep.vue';
import AssignLearnersStep from './AssignLearnersStep.vue';
import AssignResult from './AssignResult.vue';
import { createAssignSchema, localDay, maximumLearners } from './assignSchema';
import { type DueDateCode, useAssignCourse } from './useAssignCourse';

// Assigning a course (#37): a course from the catalog, the learners, a due date with an optional yearly recurrence,
// a review, then the result. One step shows at a time; the state is the page's and belongs to the current pharmacy.
const steps = ['course', 'learners', 'due', 'review'] as const;
type Step = (typeof steps)[number] | 'result';

const stepTitles: Record<Step, MessageKey> = {
  course: 'compliance.assign.course.title',
  learners: 'compliance.assign.learners.title',
  due: 'compliance.assign.due.title',
  review: 'compliance.assign.review.title',
  result: 'compliance.assign.result.title',
};

const dueDateMessages: Record<DueDateCode, MessageKey> = {
  'compliance.due-date-in-the-past': 'compliance.assign.due.inThePast',
  'compliance.due-date-too-far': 'compliance.assign.due.tooFar',
  'compliance.due-date-missing': 'compliance.assign.due.required',
};

const dueField = 'assign-due-date';
const repeatField = 'assign-repeat-yearly';

const clock = () => new Date();
const schema = createAssignSchema(clock);

const { t, d, n } = useI18n();
const { current } = useCurrentPharmacy();
const { assign, isAssigning } = useAssignCourse();

const step = ref<Step>('course');
const course = ref<CatalogCourseDto>();
/** The people selected, each id with the name, in the order they were selected. */
const selected = ref(new Map<string, string>());
/** A refused Next on the course or learners step, or a course the host no longer assigns. */
const stepError = ref<{ key: MessageKey; values?: Record<string, unknown> }>();
const reviewError = ref<MessageKey>();
/** The host's words when the user cannot assign here at all. */
const unavailable = ref<string>();
const outcome = ref<{ assigned: number; refused: ComplianceRefusedLearnerDto[] }>();
const sending = ref(false);

const { defineField, errors, validate, resetForm, setFieldError, values } = useForm({
  validationSchema: toTypedSchema(schema.pick({ dueDate: true, repeatYearly: true })),
  initialValues: { dueDate: '', repeatYearly: false },
});
const [dueDate, dueDateAttributes] = defineField('dueDate');
const [repeatYearly, repeatYearlyAttributes] = defineField('repeatYearly');

const heading = useTemplateRef<HTMLElement>('heading');
const alert = useTemplateRef<HTMLElement>('alert');
const dueAlert = useTemplateRef<HTMLElement>('dueAlert');

const today = computed(() => localDay(clock()));
const learners = computed(() => [...selected.value]);
const count = computed(() => selected.value.size);

async function focus(element: () => HTMLElement | null) {
  await nextTick();
  element()?.focus();
}

/** Shows the step, its heading focused. */
async function enter(next: Step) {
  step.value = next;
  await focus(() => heading.value);
}

/** Starts afresh: on the course step, nothing chosen. */
function reset() {
  step.value = 'course';
  course.value = undefined;
  selected.value = new Map();
  stepError.value = undefined;
  reviewError.value = undefined;
  unavailable.value = undefined;
  outcome.value = undefined;
  resetForm({ values: { dueDate: '', repeatYearly: false } });
}

// Switching the pharmacy starts the wizard afresh: the selection is of another pharmacy's members.
watch(() => current.value?.id, reset);

async function refuse(key: MessageKey, refusalValues?: Record<string, unknown>) {
  stepError.value = { key, values: refusalValues };
  await focus(() => alert.value);
}

function choose(chosen: CatalogCourseDto) {
  course.value = chosen;
  stepError.value = undefined;
}

function toggle(member: ComplianceMemberDto) {
  if (!selected.value.delete(member.personId)) {
    selected.value.set(member.personId, member.name);
  }
  stepError.value = undefined;
}

function add(members: ComplianceMemberDto[]) {
  for (const member of members) {
    selected.value.set(member.personId, member.name);
  }
  stepError.value = undefined;
}

function clear() {
  selected.value.clear();
}

async function next() {
  if (step.value === 'course') {
    const checked = schema.shape.courseId.safeParse(course.value?.courseId ?? '');
    if (!checked.success) {
      await refuse('compliance.assign.course.required');
      return;
    }
    await enter('learners');
  } else if (step.value === 'learners') {
    const checked = schema.shape.personIds.safeParse([...selected.value.keys()]);
    if (!checked.success) {
      const key = (checked.error.issues[0]?.message ?? 'compliance.assign.learners.required') as MessageKey;
      await refuse(key, { max: maximumLearners });
      return;
    }
    await enter('due');
  } else if (step.value === 'due') {
    const { valid } = await validate();
    if (!valid) {
      await focus(() => dueAlert.value);
      return;
    }
    await enter('review');
  }
}

async function back() {
  stepError.value = undefined;
  reviewError.value = undefined;
  const index = steps.findIndex((each) => each === step.value);
  const previous = steps[index - 1];
  if (previous !== undefined) {
    await enter(previous);
  }
}

/** Sends the assignment once, however often Submit is pressed, and lands where its answer can be acted on. */
async function submit() {
  const pharmacy = current.value;
  if (sending.value || pharmacy === undefined || course.value === undefined) {
    return;
  }
  sending.value = true;
  reviewError.value = undefined;
  try {
    const answer = await assign(pharmacy.id, {
      courseId: course.value.courseId,
      personIds: [...selected.value.keys()],
      dueDate: values.dueDate,
      recurrenceMonths: values.repeatYearly ? 12 : null,
    });
    switch (answer.kind) {
      case 'done': {
        outcome.value = { assigned: answer.assigned.length, refused: answer.refused };
        if (answer.assigned.length > 0) {
          const assigned = answer.assigned.length;
          notify.success(translate('compliance.assign.result.assigned', { count: assigned }, assigned));
        } else {
          notify.failure(translate('compliance.assign.result.noneAssigned'));
        }
        await enter('result');
        break;
      }
      case 'dueDate':
        step.value = 'due';
        setFieldError('dueDate', dueDateMessages[answer.code]);
        await focus(() => dueAlert.value);
        break;
      case 'courseGone':
        course.value = undefined;
        step.value = 'course';
        await refuse('compliance.assign.course.gone');
        break;
      case 'notAvailable':
        unavailable.value = answer.message;
        break;
      case 'changed':
        reviewError.value = 'compliance.assign.changed';
        break;
      case 'failed':
        reviewError.value = 'compliance.assign.failed';
        break;
    }
  } finally {
    sending.value = false;
  }
}

async function again() {
  reset();
  await focus(() => heading.value);
}

function nameOf(personId: string) {
  return selected.value.get(personId) ?? personId;
}
</script>

<template>
  <main class="flex max-w-4xl flex-col gap-6">
    <h1 class="text-title">{{ t('modules.compliance.pages.assign.title') }}</h1>
    <p v-if="current?.canAssignCourses === false" class="text-body">{{ t('compliance.assign.notAllowed') }}</p>
    <p v-else-if="unavailable !== undefined" role="alert" class="text-body">{{ unavailable }}</p>
    <template v-else-if="current !== undefined">
      <ol :aria-label="t('compliance.assign.steps.label')" class="flex flex-wrap gap-2 text-caption">
        <li
          v-for="(each, index) in steps"
          :key="each"
          :aria-current="step === each ? 'step' : undefined"
          class="rounded-md border px-2 py-1 text-muted-foreground aria-[current=step]:border-primary aria-[current=step]:font-medium aria-[current=step]:text-foreground"
        >
          <span class="tabular-nums">{{ n(index + 1, 'integer') }}</span> {{ t(`compliance.assign.steps.${each}`) }}
        </li>
      </ol>
      <section :aria-labelledby="`assign-${step}`" class="flex flex-col gap-4">
        <h2 :id="`assign-${step}`" ref="heading" tabindex="-1" class="text-body font-medium outline-none">
          {{ t(stepTitles[step]) }}
        </h2>
        <p
          v-if="stepError !== undefined"
          ref="alert"
          role="alert"
          tabindex="-1"
          class="text-body text-destructive outline-none"
        >
          {{ t(stepError.key, stepError.values ?? {}) }}
        </p>

        <AssignCourseStep v-if="step === 'course'" :chosen="course?.courseId" @choose="choose" />
        <AssignLearnersStep
          v-else-if="step === 'learners'"
          :pharmacy-id="current.id"
          :selected="selected"
          @toggle="toggle"
          @add="add"
          @clear="clear"
        />
        <div v-else-if="step === 'due'" class="flex flex-col gap-4">
          <div class="flex flex-col gap-2">
            <label :for="dueField" class="text-body font-medium">{{ t('compliance.assign.due.date') }}</label>
            <input
              :id="dueField"
              v-model="dueDate"
              v-bind="dueDateAttributes"
              type="date"
              :min="today"
              aria-required="true"
              :aria-invalid="errors.dueDate ? true : undefined"
              :aria-describedby="errors.dueDate ? `${dueField}-error` : undefined"
              class="h-9 w-fit rounded-md border border-input bg-transparent px-3 py-1 text-body outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive dark:bg-input/30"
            />
            <p
              v-if="errors.dueDate"
              :id="`${dueField}-error`"
              ref="dueAlert"
              role="alert"
              tabindex="-1"
              class="text-caption text-destructive outline-none"
            >
              {{ t(errors.dueDate) }}
            </p>
          </div>
          <div class="flex items-center gap-2">
            <input
              :id="repeatField"
              v-model="repeatYearly"
              v-bind="repeatYearlyAttributes"
              type="checkbox"
              class="size-4 accent-primary"
            />
            <label :for="repeatField" class="text-body">{{ t('compliance.assign.due.repeat') }}</label>
          </div>
        </div>
        <div v-else-if="step === 'review'" class="flex flex-col gap-4">
          <dl class="grid gap-x-4 gap-y-2 text-body sm:grid-cols-[auto_1fr]">
            <dt class="font-medium">{{ t('compliance.assign.review.course') }}</dt>
            <dd>{{ course?.title }}</dd>
            <dt class="font-medium">{{ t('compliance.assign.review.learners', { count }) }}</dt>
            <dd>
              <ul class="flex flex-wrap gap-x-3 gap-y-1">
                <li v-for="[personId, name] in learners" :key="personId">{{ name }}</li>
              </ul>
            </dd>
            <dt class="font-medium">{{ t('compliance.assign.review.due') }}</dt>
            <dd>
              <template v-if="values.dueDate">{{ d(calendarDate(values.dueDate), 'long') }}</template>
            </dd>
            <dt class="font-medium">{{ t('compliance.assign.review.recurrence') }}</dt>
            <dd>
              {{ values.repeatYearly ? t('compliance.assign.review.yearly') : t('compliance.assign.review.once') }}
            </dd>
          </dl>
          <p v-if="reviewError !== undefined" role="alert" class="text-body text-destructive">{{ t(reviewError) }}</p>
        </div>
        <AssignResult
          v-else-if="outcome !== undefined"
          :assigned="outcome.assigned"
          :refused="outcome.refused"
          :name-of="nameOf"
          @again="again"
        />

        <div v-if="step !== 'result'" class="flex flex-wrap gap-2">
          <Button v-if="step !== 'course'" type="button" variant="outline" @click="back">
            {{ t('compliance.assign.back') }}
          </Button>
          <Button v-if="step === 'review'" type="button" :disabled="sending || isAssigning" @click="submit">
            {{ t('compliance.assign.submit') }}
          </Button>
          <Button v-else type="button" @click="next">{{ t('compliance.assign.next') }}</Button>
        </div>
      </section>
    </template>
  </main>
</template>
