<script setup lang="ts">
import { History } from '@lucide/vue';
import { computed, h, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import type { EditorialCourseVersionReturnDto } from '@/api/generated';
import DataTable from '@/components/ui/DataTable.vue';
import { columnsOf, translatedHeader } from '@/components/ui/data-table-columns';
import { EmptyState } from '@/components/ui/empty-state';
import { i18n } from '@/i18n';
import { useCourseVersionReturns } from './useCourseVersionReturns';

// A version's return history: when it went back to draft and why, newest first as the host answers, a page at a time.
// The reason is the editor's text as typed: a text child, so markup in it shows as the characters it is.
const returnsOf = columnsOf<EditorialCourseVersionReturnDto>();
const columns = returnsOf.columns([
  returnsOf.accessor('returnedAt', {
    ...translatedHeader('editorial.returns.columns.returnedAt'),
    cell: ({ row }) => i18n.global.d(new Date(row.original.returnedAt), 'dateTime'),
  }),
  returnsOf.accessor('reason', {
    ...translatedHeader('editorial.returns.columns.reason'),
    cell: ({ row }) => h('span', { class: 'whitespace-pre-line break-words' }, row.original.reason),
  }),
]);
const none: EditorialCourseVersionReturnDto[] = [];

const props = defineProps<{ versionId: string }>();

// The page shown is this component's own, not the address's; it belongs to the version it was turned for, so another
// version starts at its first page.
const turned = ref({ versionId: props.versionId, page: 1 });
const page = computed(() => (turned.value.versionId === props.versionId ? turned.value.page : 1));
const { returns, page: paging, isPending, isError } = useCourseVersionReturns(() => props.versionId, page);
const { t } = useI18n();

/** Shows another page of the history. */
function turnTo(next: number) {
  turned.value = { versionId: props.versionId, page: next };
}

/** Takes the history back to its first page, where a new return shows. */
function showFirstPage() {
  turnTo(1);
}

defineExpose({ showFirstPage });
</script>

<template>
  <section aria-labelledby="course-version-returns" class="flex flex-col gap-4">
    <h2 id="course-version-returns">{{ t('editorial.returns.title') }}</h2>
    <p v-if="isError" role="alert">{{ t('editorial.returns.loadFailed') }}</p>
    <DataTable
      v-else
      :columns="columns"
      :data="returns ?? none"
      :loading="isPending"
      :paging="paging"
      @update:page="turnTo"
    >
      <template #empty>
        <EmptyState>
          <template #icon><History /></template>
          {{ t('editorial.returns.empty') }}
        </EmptyState>
      </template>
    </DataTable>
  </section>
</template>
