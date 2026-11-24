<script setup lang="ts">
import { computed, useId, useTemplateRef } from 'vue';
import { useI18n } from 'vue-i18n';
import type { ComplianceMyAssignmentDto, PagedResultOfComplianceMyAssignmentDto } from '@/api/generated';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import MyCourseItem from './MyCourseItem.vue';

// My courses as a list of items, not DataTable (#38, Q3): Start or Continue always shows, and an overdue course is
// highlighted. The items keep the host's order. While the first page loads, three skeleton items in their shape take
// the section's place, so its heading comes with the list; with nothing assigned, the empty slot; below the items,
// which of how many they are, which describes the list, and Previous and Next for more than one page.
const props = defineProps<{
  items: ComplianceMyAssignmentDto[];
  /** The page of courses, as the host answers it; none while it loads. */
  paging: PagedResultOfComplianceMyAssignmentDto | undefined;
  /** The first load: skeleton items in the list's place. */
  pending: boolean;
  /** A load of any kind, the first or another page's. */
  busy: boolean;
}>();

const emit = defineEmits<{ 'update:page': [page: number] }>();

defineSlots<{ empty: () => unknown }>();

const { t, n } = useI18n();
const headingId = useId();
const summaryId = useId();
const heading = useTemplateRef<HTMLElement>('heading');

const empty = computed(() => props.paging !== undefined && Number(props.paging.totalCount) === 0);

/** "Einträge 1–5 von 5", or nothing while loading or for a page without courses. */
const summary = computed(() => {
  const { paging } = props;
  if (props.pending || paging === undefined || props.items.length === 0) {
    return undefined;
  }
  const first = (Number(paging.page) - 1) * Number(paging.pageSize) + 1;
  return t('dataTable.showing', {
    first: n(first, 'integer'),
    last: n(first + props.items.length - 1, 'integer'),
    total: n(Number(paging.totalCount), 'integer'),
  });
});

/** The page shown and how many there are, for more courses than a page holds. */
const pager = computed(() => {
  const { paging } = props;
  if (paging === undefined) {
    return undefined;
  }
  const page = Number(paging.page);
  const pages = Math.ceil(Number(paging.totalCount) / Number(paging.pageSize));
  return pages > 1 ? { page, pages } : undefined;
});

function focusHeading() {
  heading.value?.focus();
}

function goTo(page: number) {
  emit('update:page', page);
  focusHeading();
}

defineExpose({ focusHeading });
</script>

<template>
  <div v-if="pending" class="flex flex-col gap-4">
    <p class="sr-only" role="status">{{ t('dataTable.loading') }}</p>
    <div class="flex max-w-3xl flex-col gap-3">
      <div
        v-for="item in 3"
        :key="item"
        aria-hidden="true"
        class="flex flex-wrap items-center justify-between gap-4 rounded-md border bg-background p-4"
      >
        <div class="flex min-w-0 flex-1 flex-col gap-2">
          <Skeleton class="h-5 w-2/3" />
          <Skeleton class="h-4 w-40" />
        </div>
        <Skeleton class="h-9 w-28" />
      </div>
    </div>
  </div>
  <section v-else :aria-labelledby="headingId" class="flex min-w-0 flex-col gap-4">
    <h2 :id="headingId" ref="heading" tabindex="-1" class="text-body font-semibold outline-none">
      {{ t('learning.myCourses.list.title') }}
    </h2>
    <slot v-if="empty" name="empty" />
    <template v-else>
      <ul
        role="list"
        class="flex max-w-3xl flex-col gap-3"
        :aria-busy="busy || undefined"
        :aria-describedby="summary === undefined ? undefined : summaryId"
      >
        <MyCourseItem v-for="assignment in items" :key="assignment.assignmentId" :assignment="assignment" />
      </ul>
      <p v-if="summary !== undefined" :id="summaryId" class="text-caption text-muted-foreground tabular-nums">
        {{ summary }}
      </p>
      <nav v-if="pager !== undefined" :aria-label="t('dataTable.pagination')" class="flex max-w-3xl justify-end gap-2">
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
    </template>
  </section>
</template>
