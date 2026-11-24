<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import type { ComplianceMatrixCourseDto, ComplianceMatrixRowDto } from '@/api/generated';
import AssignmentStatusLabel from '@/components/status/AssignmentStatusLabel.vue';
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet';
import { calendarDate } from '@/i18n/formats';
import { cellStatus } from './cellStatus';

// A member's training status, from the row already loaded: every course assigned to them in the matrix's column order,
// whatever the course filter, with its status, its due date, when it was last completed and until when that holds. It
// asks the host nothing and has no address of its own. Closing it gives the focus back to the name that opened it.
const props = defineProps<{
  /** The member shown; none while the sheet is closed. */
  row: ComplianceMatrixRowDto | undefined;
  /** Every column of the matrix, not only those the course filter shows. */
  courses: ComplianceMatrixCourseDto[];
  today: Date;
  /** Where the focus returns once the sheet closes. */
  opener: HTMLElement | undefined;
}>();

const emit = defineEmits<{ close: [] }>();

const { t, d } = useI18n();

/** The member's courses, in the columns' order, each with its title and status. */
const sections = computed(() => {
  const row = props.row;
  if (row === undefined) {
    return [];
  }
  return props.courses.flatMap((course) => {
    const cell = row.cells.find((each) => each.courseId === course.courseId);
    return cell === undefined ? [] : [{ course, cell, status: cellStatus(cell, props.today) }];
  });
});

const description = computed(() => {
  const row = props.row;
  if (row === undefined) {
    return '';
  }
  const count = sections.value.length;
  return `${t(`glossary.jobFunction.${row.jobFunction}`)} · ${t('compliance.matrix.person.count', { count }, count)}`;
});

const dateOf = (value: string) => d(calendarDate(value), 'date');

function changeOpen(open: boolean) {
  if (!open) {
    emit('close');
  }
}

function returnFocus(event: Event) {
  if (props.opener !== undefined) {
    event.preventDefault();
    props.opener.focus();
  }
}
</script>

<template>
  <Sheet :open="row !== undefined" @update:open="changeOpen">
    <SheetContent side="right" class="overflow-y-auto" @close-auto-focus="returnFocus">
      <template v-if="row !== undefined">
        <SheetHeader>
          <SheetTitle class="text-title">{{ row.name }}</SheetTitle>
          <SheetDescription class="text-body">{{ description }}</SheetDescription>
        </SheetHeader>
        <div class="flex flex-col gap-4 px-4 pb-4">
          <p v-if="sections.length === 0" class="text-body text-muted-foreground">
            {{ t('compliance.matrix.person.none') }}
          </p>
          <section
            v-for="{ course, cell, status } in sections"
            :key="course.courseId"
            class="flex flex-col gap-2 rounded-md border p-3"
          >
            <h3 class="text-body font-semibold">{{ course.title }}</h3>
            <AssignmentStatusLabel :status="status" />
            <dl class="text-body grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
              <dt class="text-muted-foreground">{{ t('compliance.matrix.person.due') }}</dt>
              <dd class="tabular-nums">{{ dateOf(cell.dueDate) }}</dd>
              <dt class="text-muted-foreground">{{ t('compliance.matrix.person.lastCompleted') }}</dt>
              <dd class="tabular-nums">
                {{ cell.lastCompletedOn === null ? t('compliance.matrix.person.never') : dateOf(cell.lastCompletedOn) }}
              </dd>
              <template v-if="cell.validUntil !== null">
                <dt class="text-muted-foreground">{{ t('compliance.matrix.person.validUntil') }}</dt>
                <dd class="tabular-nums">{{ dateOf(cell.validUntil) }}</dd>
              </template>
            </dl>
          </section>
        </div>
      </template>
    </SheetContent>
  </Sheet>
</template>
