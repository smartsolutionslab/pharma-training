<script setup lang="ts">
import { computed, useId } from 'vue';
import { useI18n } from 'vue-i18n';
import type { ComplianceMyAssignmentDto } from '@/api/generated';
import AssignmentStatusLabel from '@/components/status/AssignmentStatusLabel.vue';
import { Button } from '@/components/ui/button';
import { calendarDate } from '@/i18n/formats';
import { cn } from '@/lib/utils';
import { coursePath } from './coursePath';

// One of my courses: its title, its status as an icon and a word, its due line, and the one action, Start or Continue,
// a link to the chapter player on the version Compliance names (ADR 0024). An overdue course is highlighted by its
// word, its icon, its red due line and a thick left border, each of which says "overdue" without the others.
const props = defineProps<{ assignment: ComplianceMyAssignmentDto }>();

const { t, d } = useI18n();
const titleId = useId();

const overdue = computed(() => props.assignment.state === 'Overdue');
const dueLine = computed(() =>
  t(overdue.value ? 'learning.myCourses.dueOverdue' : 'learning.myCourses.due', {
    date: d(calendarDate(props.assignment.dueDate), 'date'),
  }),
);

/** Where Start or Continue leads, and what it says; none without a version to open. */
const action = computed(() => {
  const { startCourseVersionId, continuesSession, courseTitle } = props.assignment;
  if (startCourseVersionId === null) {
    return undefined;
  }
  return {
    to: coursePath(startCourseVersionId),
    word: t(continuesSession ? 'learning.myCourses.continue' : 'learning.myCourses.start'),
    name: t(continuesSession ? 'learning.myCourses.continueCourse' : 'learning.myCourses.startCourse', {
      title: courseTitle,
    }),
  };
});
</script>

<template>
  <li
    :class="
      cn(
        'flex flex-wrap items-center justify-between gap-4 rounded-md border bg-background p-4',
        overdue && 'border-l-4 border-l-destructive',
      )
    "
  >
    <div class="flex min-w-0 flex-col gap-1">
      <h3 :id="titleId" class="text-body font-semibold break-words">{{ assignment.courseTitle }}</h3>
      <div class="text-caption flex flex-wrap items-center gap-x-3">
        <AssignmentStatusLabel :status="assignment.state" />
        {{ ' ' }}
        <span :class="cn('tabular-nums', overdue ? 'text-destructive font-medium' : 'text-muted-foreground')">{{
          dueLine
        }}</span>
      </div>
      <p v-if="assignment.state === 'Locked'" class="text-caption text-muted-foreground">
        {{ t('learning.myCourses.locked') }}
      </p>
    </div>
    <Button v-if="action !== undefined" as-child>
      <RouterLink :to="action.to" :aria-label="action.name">{{ action.word }}</RouterLink>
    </Button>
    <p v-else class="text-caption text-muted-foreground">{{ t('learning.myCourses.notStartable') }}</p>
  </li>
</template>
