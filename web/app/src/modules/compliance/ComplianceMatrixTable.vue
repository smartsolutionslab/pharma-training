<script setup lang="ts">
import { computed, useId, useTemplateRef } from 'vue';
import { useI18n } from 'vue-i18n';
import type {
  ComplianceMatrixCellDto,
  ComplianceMatrixCourseDto,
  ComplianceMatrixRowDto,
  PagedResultOfComplianceMatrixRowDto,
} from '@/api/generated';
import { statusLooks } from '@/components/status/assignmentStatus';
import AssignmentStatusLabel from '@/components/status/AssignmentStatusLabel.vue';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { calendarDate } from '@/i18n/formats';
import { cn } from '@/lib/utils';
import { cellStatus } from './cellStatus';
import { rateOf } from './useTeamMatrix';
import { rateFigure } from './matrixRate';

// The team matrix: a table of its own, not DataTable, since its columns are the courses only the answer knows. A
// column header per course and a row header per member name each cell for a screen reader. It scrolls inside a
// focusable region, with the member column pinned left and the course headers pinned on top. Below it, which rows it
// shows of how many, which describes the region, and Previous and Next, which move the focus to the region.
const props = defineProps<{
  columns: ComplianceMatrixCourseDto[];
  rows: ComplianceMatrixRowDto[];
  /** The page of rows, as the host answers it; none while the matrix loads. */
  paging: PagedResultOfComplianceMatrixRowDto | undefined;
  /** The first load: skeleton rows in the table's place. */
  pending: boolean;
  /** A load of any kind, the first or another page's. */
  busy: boolean;
  /** Today, which tells an expired completion from a valid one. */
  today: Date;
}>();

const emit = defineEmits<{
  'update:page': [page: number];
  /** A member's name was chosen: their sheet opens, and the focus returns to the name when it closes. */
  open: [personId: string, opener: HTMLElement];
}>();

const { t, n, d } = useI18n();
const summaryId = useId();
const region = useTemplateRef<HTMLElement>('region');

/** "Einträge 1–50 von 60", or nothing while loading or for a page without rows. */
const summary = computed(() => {
  const { paging } = props;
  if (props.pending || paging === undefined || props.rows.length === 0) {
    return undefined;
  }
  const first = (Number(paging.page) - 1) * Number(paging.pageSize) + 1;
  return t('dataTable.showing', {
    first: n(first, 'integer'),
    last: n(first + props.rows.length - 1, 'integer'),
    total: n(Number(paging.totalCount), 'integer'),
  });
});

/** The page shown and how many there are, for a team longer than a page. */
const pager = computed(() => {
  const { paging } = props;
  if (paging === undefined) {
    return undefined;
  }
  const page = Number(paging.page);
  const pages = Math.ceil(Number(paging.totalCount) / Number(paging.pageSize));
  return pages > 1 ? { page, pages } : undefined;
});

/** Each row with a cell per column shown: its status and date line, or none for a course not assigned to them. */
const lines = computed(() =>
  props.rows.map((row) => ({
    ...row,
    cells: props.columns.map((course) => {
      const cell = row.cells.find((each) => each.courseId === course.courseId);
      return { courseId: course.courseId, look: cell === undefined ? undefined : lookOf(cell) };
    }),
  })),
);

function courseRate(course: ComplianceMatrixCourseDto): string {
  const rate = rateOf(course.complianceRate);
  return t('compliance.matrix.table.courseRate', {
    rate: rate === null ? t('compliance.matrix.rate.noValue') : n(rateFigure(rate).value, rateFigure(rate).format),
  });
}

function lookOf(cell: ComplianceMatrixCellDto) {
  const status = cellStatus(cell, props.today);
  const look = statusLooks[status];
  const date = look.line === 'validUntil' ? cell.validUntil : cell.dueDate;
  const line =
    date === null
      ? undefined
      : t(look.line === 'validUntil' ? 'compliance.matrix.table.validUntil' : 'compliance.matrix.table.due', {
          date: d(calendarDate(date), 'date'),
        });
  return { status, line, needsAction: look.needsAction };
}

function open(row: { personId: string }, event: MouseEvent) {
  emit('open', row.personId, event.currentTarget as HTMLElement);
}

function goTo(page: number) {
  emit('update:page', page);
  region.value?.focus();
}

const cellClass = 'border-b px-3 py-2 align-top';
</script>

<template>
  <div class="flex flex-col gap-2">
    <div
      ref="region"
      role="region"
      :aria-label="t('compliance.matrix.table.label')"
      tabindex="0"
      :aria-busy="busy || undefined"
      :aria-describedby="summary === undefined ? undefined : summaryId"
      class="max-h-[70vh] overflow-auto rounded-md border focus-visible:ring-3 focus-visible:ring-ring/50 outline-none"
    >
      <div v-if="pending" aria-hidden="true" class="flex flex-col gap-2 p-4">
        <div v-for="row in 3" :key="row" class="flex gap-4">
          <Skeleton v-for="column in 4" :key="column" class="h-8 w-36" />
        </div>
      </div>
      <table v-else class="text-body border-separate border-spacing-0">
        <thead>
          <tr>
            <th
              scope="col"
              :class="cn(cellClass, 'sticky top-0 left-0 z-20 bg-background w-40 text-left align-bottom')"
            >
              {{ t('compliance.matrix.table.member') }}
            </th>
            <th
              v-for="course in columns"
              :key="course.courseId"
              scope="col"
              :class="cn(cellClass, 'sticky top-0 z-10 bg-background min-w-36 max-w-48 align-bottom text-left')"
            >
              <span class="block font-semibold">{{ course.title }}</span>
              {{ ' ' }}
              <span class="text-caption text-muted-foreground block font-normal tabular-nums">{{
                courseRate(course)
              }}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in lines" :key="row.personId">
            <th scope="row" :class="cn(cellClass, 'sticky left-0 z-10 bg-background w-40 text-left font-normal')">
              <Button
                type="button"
                variant="link"
                class="h-auto p-0 text-left whitespace-normal"
                :aria-label="t('compliance.matrix.table.openPerson', { name: row.name })"
                @click="open(row, $event)"
              >
                {{ row.name }}
              </Button>
              {{ ' ' }}
              <span class="text-caption text-muted-foreground block">{{
                t(`compliance.matrix.jobFunctionShort.${row.jobFunction}`)
              }}</span>
            </th>
            <td
              v-for="shown in row.cells"
              :key="shown.courseId"
              :class="
                cn(cellClass, shown.look?.needsAction === true && 'border-l-4 border-destructive border-b-border')
              "
            >
              <template v-if="shown.look === undefined">
                <span aria-hidden="true" class="text-muted-foreground">—</span>
                <span class="sr-only">{{ t('compliance.matrix.table.notAssigned') }}</span>
              </template>
              <template v-else>
                <AssignmentStatusLabel :status="shown.look.status" />
                {{ ' ' }}
                <span
                  v-if="shown.look.line !== undefined"
                  class="text-caption text-muted-foreground block tabular-nums"
                  >{{ shown.look.line }}</span
                >
              </template>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <p v-if="summary !== undefined" :id="summaryId" class="text-caption text-muted-foreground tabular-nums">
      {{ summary }}
    </p>
    <nav v-if="pager !== undefined" :aria-label="t('dataTable.pagination')" class="flex justify-end gap-2">
      <Button type="button" variant="outline" size="sm" :disabled="pager.page <= 1" @click="goTo(pager.page - 1)">
        {{ t('dataTable.previous') }}
      </Button>
      <Button
        type="button"
        variant="outline"
        size="sm"
        :disabled="pager.page >= pager.pages"
        @click="goTo(pager.page + 1)"
      >
        {{ t('dataTable.next') }}
      </Button>
    </nav>
  </div>
</template>
