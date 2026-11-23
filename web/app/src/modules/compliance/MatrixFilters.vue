<script setup lang="ts">
import { computed, useId, useTemplateRef } from 'vue';
import { useI18n } from 'vue-i18n';
import { ComplianceJobFunction, type ComplianceMatrixCourseDto } from '@/api/generated';
import { Button } from '@/components/ui/button';

// The matrix's filters: a course of the matrix's columns (the page's own) and a job function (the host's). Each is a
// native select; the page puts the choice in the address. Reset shows while either is set.
const props = defineProps<{
  courses: ComplianceMatrixCourseDto[];
  /** The course chosen, if the matrix has it. */
  course: string | undefined;
  jobFunction: ComplianceJobFunction | undefined;
}>();

const emit = defineEmits<{
  'update:course': [course: string | undefined];
  'update:jobFunction': [jobFunction: ComplianceJobFunction | undefined];
  reset: [];
}>();

const { t } = useI18n();
const id = useId();
const jobFunctions = Object.values(ComplianceJobFunction);
const jobFunctionSelect = useTemplateRef<HTMLSelectElement>('jobFunctionSelect');

const selectClass =
  'h-9 min-w-0 rounded-md border border-input bg-background px-3 py-1 text-body outline-none motion-safe:transition-[color,box-shadow] focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30 [&_option]:bg-popover [&_option]:text-popover-foreground';

// Native selects bound through v-model, so a choice from the address shows once its options arrive.
const chosenCourse = computed({
  get: () => props.course ?? '',
  set: (value: string) => emit('update:course', value === '' ? undefined : value),
});
const chosenJobFunction = computed({
  get: () => props.jobFunction ?? '',
  set: (value: string) =>
    emit(
      'update:jobFunction',
      jobFunctions.find((jobFunction) => jobFunction === value),
    ),
});

defineExpose({
  /** Puts the focus on the job function, where a reset leaves the manager. */
  focusJobFunction: () => jobFunctionSelect.value?.focus(),
});
</script>

<template>
  <fieldset class="flex flex-wrap items-end gap-4">
    <legend class="sr-only">{{ t('compliance.matrix.filters.label') }}</legend>
    <div class="flex flex-col gap-1">
      <label :for="`${id}-course`" class="text-caption font-medium">{{ t('compliance.matrix.filters.course') }}</label>
      <select :id="`${id}-course`" v-model="chosenCourse" :class="selectClass">
        <option value="">{{ t('compliance.matrix.filters.allCourses') }}</option>
        <option v-for="each in courses" :key="each.courseId" :value="each.courseId">{{ each.title }}</option>
      </select>
    </div>
    <div class="flex flex-col gap-1">
      <label :for="`${id}-job-function`" class="text-caption font-medium">{{
        t('compliance.matrix.filters.jobFunction')
      }}</label>
      <select :id="`${id}-job-function`" ref="jobFunctionSelect" v-model="chosenJobFunction" :class="selectClass">
        <option value="">{{ t('compliance.matrix.filters.allJobFunctions') }}</option>
        <option v-for="each in jobFunctions" :key="each" :value="each">{{ t(`glossary.jobFunction.${each}`) }}</option>
      </select>
    </div>
    <Button
      v-if="course !== undefined || jobFunction !== undefined"
      type="button"
      variant="ghost"
      @click="emit('reset')"
    >
      {{ t('compliance.matrix.filters.reset') }}
    </Button>
  </fieldset>
</template>
