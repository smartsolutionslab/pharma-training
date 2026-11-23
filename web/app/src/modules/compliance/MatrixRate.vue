<script setup lang="ts">
import { computed, useId } from 'vue';
import { useI18n } from 'vue-i18n';
import type { ComplianceJobFunction } from '@/api/generated';
import { Skeleton } from '@/components/ui/skeleton';
import type { MatrixTally } from './useTeamMatrix';

// The compliance rate: the share of the assignments on track, over the filter (the job function, and the course if one
// is chosen), with its counts and what it covers; skeletons in its shape while the matrix loads. Its figures are announced politely, so a filter's new ones are heard.
const props = defineProps<{
  /** The figures; none while the matrix loads. */
  tally: MatrixTally | undefined;
  /** The title of the course chosen, if one is. */
  course: string | undefined;
  jobFunction: ComplianceJobFunction | undefined;
}>();

const { t, n } = useI18n();
const headingId = useId();

const scope = computed(() =>
  t('compliance.matrix.rate.scope', {
    course: props.course ?? t('compliance.matrix.filters.allCourses'),
    jobFunction:
      props.jobFunction === undefined
        ? t('compliance.matrix.filters.allJobFunctions')
        : t(`glossary.jobFunction.${props.jobFunction}`),
  }),
);
</script>

<template>
  <div v-if="tally === undefined" aria-hidden="true" class="flex flex-col gap-2">
    <Skeleton class="h-5 w-40" />
    <Skeleton class="h-12 w-24" />
    <Skeleton class="h-4 w-48" />
    <Skeleton class="h-4 w-40" />
  </div>
  <section v-else :aria-labelledby="headingId" class="flex flex-col gap-2">
    <h2 :id="headingId" class="text-body font-semibold">{{ t('compliance.matrix.rate.title') }}</h2>
    <div aria-live="polite" class="flex flex-col gap-1">
      <p class="text-display tabular-nums">
        {{ tally.rate === null ? t('compliance.matrix.rate.noValue') : n(tally.rate, 'percent') }}
      </p>
      <p v-if="tally.rate === null" class="text-body">{{ t('compliance.matrix.rate.none') }}</p>
      <template v-else>
        <p class="text-body tabular-nums">
          {{
            t('compliance.matrix.rate.summary', {
              compliant: n(tally.compliant, 'integer'),
              assignments: n(tally.assignments, 'integer'),
            })
          }}
        </p>
        <p class="text-body tabular-nums">
          {{ t('compliance.matrix.rate.overdue', { count: n(tally.overdue, 'integer') }, tally.overdue) }}
        </p>
      </template>
      <p class="text-body text-muted-foreground">{{ scope }}</p>
    </div>
    <p class="text-caption text-muted-foreground">{{ t('compliance.matrix.rate.explanation') }}</p>
  </section>
</template>
