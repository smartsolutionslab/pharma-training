<script setup lang="ts">
import { History } from '@lucide/vue';
import { computed, h, nextTick, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useAuth } from '@/auth/useAuth';
import type { EditorialCourseVersionReturnDto } from '@/api/generated';
import DataTable from '@/components/ui/DataTable.vue';
import { columnsOf, translatedHeader } from '@/components/ui/data-table-columns';
import { EmptyState } from '@/components/ui/empty-state';
import { i18n } from '@/i18n';
import { useCourseVersionReturns } from './useCourseVersionReturns';

// A version's return history: when it went back to draft and why, newest first as the host answers, a page at a time.
// The reason is the editor's text as typed: a text child, so markup in it shows as the characters it is.
// Who returned it: the name the host stored with the return (#220), as text. A return stored before names were has
// none; it reads as the signed-in editor's or another editor's. A row carries whether it is the signed-in editor's, so
// the columns stay at module scope.
type ReturnRow = EditorialCourseVersionReturnDto & { byYou: boolean };
const returnsOf = columnsOf<ReturnRow>();
const columns = returnsOf.columns([
  returnsOf.accessor('returnedAt', {
    ...translatedHeader('editorial.returns.columns.returnedAt'),
    cell: ({ row }) => i18n.global.d(new Date(row.original.returnedAt), 'dateTime'),
  }),
  returnsOf.accessor('byYou', {
    ...translatedHeader('editorial.returns.columns.returnedBy'),
    cell: ({ row }) => {
      const name = row.original.returnedByName?.trim() ?? '';
      if (name !== '') {
        return name;
      }
      return i18n.global.t(row.original.byYou ? 'editorial.returns.byYou' : 'editorial.returns.byAnotherEditor');
    },
  }),
  returnsOf.accessor('reason', {
    ...translatedHeader('editorial.returns.columns.reason'),
    cell: ({ row }) => h('span', { class: 'whitespace-pre-line break-words' }, row.original.reason),
  }),
]);
const none: ReturnRow[] = [];

const props = defineProps<{ versionId: string }>();

// The page shown is this component's own, not the address's. The route reuses this component for another version,
// which starts at its first page, as does a version come back to: the page goes back to 1 as soon as the id changes,
// before anything is asked of the host for it.
const page = ref(1);
watch(
  () => props.versionId,
  () => {
    page.value = 1;
  },
  { flush: 'sync' },
);
const { returns, page: paging, isPending, isError } = useCourseVersionReturns(() => props.versionId, page);
const { t } = useI18n();
const { user } = useAuth();
const rows = computed(() => {
  const you = user.value?.profile.sub;
  return returns.value?.map((each) => ({ ...each, byYou: you !== undefined && you !== '' && each.returnedBy === you }));
});

/** Shows another page of the history. */
function turnTo(next: number) {
  page.value = next;
}

/**
 * Takes the history back to its first page, where a new return shows. Resolves once the history's query is that
 * page's, so the returns read again after it are the first page's.
 */
async function showFirstPage() {
  turnTo(1);
  await nextTick();
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
      :data="rows ?? none"
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
