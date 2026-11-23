<script setup lang="ts">
import { Users } from '@lucide/vue';
import { computed, h, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink } from 'vue-router';
import {
  ComplianceJobFunction,
  type ComplianceMemberDto,
  getCompliancePharmaciesIdMembers,
  useGetCompliancePharmaciesIdMembers,
} from '@/api/generated';
import { Button } from '@/components/ui/button';
import DataTable from '@/components/ui/DataTable.vue';
import { columnsOf, translatedHeader } from '@/components/ui/data-table-columns';
import { EmptyState } from '@/components/ui/empty-state';
import { t as translate } from '@/i18n';

// The wizard's second step: the pharmacy's active members (GET /compliance/pharmacies/{id}/members), a page at a time,
// each with a checkbox; or everyone with a job function, read page after page. The selection is the page's: it holds
// each person's name by their id, so the review and the result can name them.
const props = defineProps<{ pharmacyId: string; selected: ReadonlyMap<string, string> }>();
const emit = defineEmits<{ toggle: [member: ComplianceMemberDto]; add: [members: ComplianceMemberDto[]]; clear: [] }>();

const members = columnsOf<ComplianceMemberDto>();
const columns = members.columns([
  members.display({
    id: 'select',
    ...translatedHeader('compliance.assign.learners.columns.select'),
    cell: ({ row }) =>
      h('input', {
        type: 'checkbox',
        class: 'size-4 accent-primary',
        checked: props.selected.has(row.original.personId),
        'aria-label': translate('compliance.assign.learners.select', { name: row.original.name }),
        onChange: () => emit('toggle', row.original),
      }),
  }),
  members.accessor('name', translatedHeader('compliance.assign.learners.columns.name')),
  members.accessor('jobFunction', {
    ...translatedHeader('compliance.assign.learners.columns.jobFunction'),
    cell: ({ row }) => translate(`glossary.jobFunction.${row.original.jobFunction}`),
  }),
]);
const none: ComplianceMemberDto[] = [];
const pageSize = 20;
/** The most the host answers in a page (PageSize.Maximum). */
const largestPage = 100;
const jobFunctions = Object.values(ComplianceJobFunction);

const { t } = useI18n();
const page = ref(1);
const { data, isPending, isError, refetch } = useGetCompliancePharmaciesIdMembers(
  () => props.pharmacyId,
  () => ({ page: page.value, pageSize }),
);
const rows = computed(() => data.value?.items ?? none);
const count = computed(() => props.selected.size);

const selecting = ref(false);
const selectFailed = ref(false);

/** Reads everyone with the job function, page after page until all are read, and adds them to the selection. */
async function selectEveryone(jobFunction: ComplianceJobFunction) {
  selecting.value = true;
  selectFailed.value = false;
  try {
    const found: ComplianceMemberDto[] = [];
    for (let next = 1; ; next++) {
      const answer = await getCompliancePharmaciesIdMembers(props.pharmacyId, {
        page: next,
        pageSize: largestPage,
        jobFunction,
      });
      found.push(...answer.items);
      if (answer.items.length === 0 || found.length >= Number(answer.totalCount)) {
        break;
      }
    }
    emit('add', found);
  } catch {
    selectFailed.value = true;
  } finally {
    selecting.value = false;
  }
}
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex flex-wrap gap-2">
      <Button
        v-for="jobFunction in jobFunctions"
        :key="jobFunction"
        type="button"
        variant="outline"
        size="sm"
        :disabled="selecting"
        @click="selectEveryone(jobFunction)"
      >
        {{
          t('compliance.assign.learners.selectJobFunction', { jobFunction: t(`glossary.jobFunction.${jobFunction}`) })
        }}
      </Button>
      <Button type="button" variant="ghost" size="sm" :disabled="count === 0" @click="emit('clear')">
        {{ t('compliance.assign.learners.clear') }}
      </Button>
    </div>
    <p role="status" class="text-body tabular-nums">{{ t('compliance.assign.learners.count', { count }, count) }}</p>
    <div v-if="isError || selectFailed" role="alert" class="flex flex-col items-start gap-2">
      <p class="text-body">{{ t('compliance.assign.learners.loadFailed') }}</p>
      <Button v-if="isError" type="button" variant="outline" @click="refetch()">
        {{ t('compliance.assign.retry') }}
      </Button>
    </div>
    <DataTable
      v-if="!isError"
      :columns="columns"
      :data="rows"
      :loading="isPending"
      :paging="data"
      @update:page="(next) => (page = next)"
    >
      <template #empty>
        <EmptyState>
          <template #icon><Users /></template>
          {{ t('compliance.assign.learners.empty') }}
          <template #action>
            <Button as-child variant="outline">
              <RouterLink to="/organisation">{{ t('compliance.assign.learners.invite') }}</RouterLink>
            </Button>
          </template>
        </EmptyState>
      </template>
    </DataTable>
  </div>
</template>
