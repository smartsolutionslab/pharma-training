<script setup lang="ts">
import { BookOpen, Check } from '@lucide/vue';
import { computed, h, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { type CatalogCourseDto, useGetCatalogCourses } from '@/api/generated';
import { Button } from '@/components/ui/button';
import DataTable from '@/components/ui/DataTable.vue';
import { columnsOf, translatedHeader } from '@/components/ui/data-table-columns';
import { EmptyState } from '@/components/ui/empty-state';
import { i18n, t as translate } from '@/i18n';

// The wizard's first step: the published courses of the catalog, a page at a time, one of them chosen. The row's
// Choose button shows only while the row is hovered or has focus, so the chosen course's title carries a mark too. The
// catalog is read again each time the step shows, and a course published meanwhile comes first: the rows are the
// courses' own (by id), so a Choose button pointed at stays that course's (#477).
const props = defineProps<{ chosen?: string }>();
const emit = defineEmits<{ choose: [course: CatalogCourseDto] }>();

const courses = columnsOf<CatalogCourseDto>();
const columns = courses.columns([
  courses.accessor('title', {
    ...translatedHeader('compliance.assign.course.columns.title'),
    cell: ({ row }) =>
      props.chosen === row.original.courseId
        ? h('span', { class: 'inline-flex items-center gap-1 font-medium text-primary' }, [
            h(Check, { 'aria-hidden': 'true', class: 'size-4' }),
            row.original.title,
          ])
        : row.original.title,
  }),
  courses.accessor('versionNumber', {
    ...translatedHeader('compliance.assign.course.columns.version'),
    cell: ({ row }) => i18n.global.n(Number(row.original.versionNumber), 'integer'),
  }),
  courses.accessor('validityMonths', {
    ...translatedHeader('compliance.assign.course.columns.validity'),
    cell: ({ row }) => {
      const months = Number(row.original.validityMonths);
      return h(
        'span',
        { class: 'tabular-nums' },
        translate('compliance.assign.course.months', { count: months }, months),
      );
    },
  }),
]);
const none: CatalogCourseDto[] = [];
const pageSize = 20;

const { t } = useI18n();
const page = ref(1);
const { data, isPending, isError, refetch } = useGetCatalogCourses(() => ({ page: page.value, pageSize }));
const rows = computed(() => data.value?.items ?? none);
</script>

<template>
  <div v-if="isError" role="alert" class="flex flex-col items-start gap-2">
    <p class="text-body">{{ t('compliance.assign.course.loadFailed') }}</p>
    <Button type="button" variant="outline" @click="refetch()">{{ t('compliance.assign.retry') }}</Button>
  </div>
  <DataTable
    v-else
    :columns="columns"
    :data="rows"
    :loading="isPending"
    :row-id="(course) => course.courseId"
    :paging="data"
    @update:page="(next) => (page = next)"
  >
    <template #empty>
      <EmptyState>
        <template #icon><BookOpen /></template>
        {{ t('compliance.assign.course.empty') }}
      </EmptyState>
    </template>
    <template #actions="{ row }">
      <Button
        type="button"
        size="sm"
        :variant="props.chosen === row.courseId ? 'default' : 'outline'"
        :aria-pressed="props.chosen === row.courseId"
        @click="emit('choose', row)"
      >
        {{ t('compliance.assign.course.choose', { title: row.title }) }}
      </Button>
    </template>
  </DataTable>
</template>
