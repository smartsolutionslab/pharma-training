<script setup lang="ts">
import { Inbox } from '@lucide/vue';
import { useTemplateRef } from 'vue';
import type { AssessmentSampleDto } from '@/api/generated';
import { Button } from '@/components/ui/button';
import DataTable from '@/components/ui/DataTable.vue';
import { columnsOf } from '@/components/ui/data-table-columns';
import { EmptyState } from '@/components/ui/empty-state';
import { notify } from '@/lib/notify';
import CreateAssessmentSampleForm from './CreateAssessmentSampleForm.vue';
import { useCanManage } from './useCanManage';
import { useCreateAssessmentSample, useAssessmentSamples } from './useAssessmentSamples';

// The module's screen, starting from its sample (AGENTS.md): the samples in a table, by name, and a form that creates
// one, shown in the table at once and taken out again if the host refuses it; the result is a toast. "Manage"
// shows only to a caller its permission probe answers with 200, and any failure but a 403 shows as an alert.
const samples = columnsOf<AssessmentSampleDto>();
const columns = samples.columns([samples.accessor('name', { header: 'Name' })]);
const none: AssessmentSampleDto[] = [];

const { samples: rows, page, isLoading, isError } = useAssessmentSamples();
const { create, isSaving } = useCreateAssessmentSample();
const permission = useCanManage();
const form = useTemplateRef('form');

async function onCreate(name: string) {
  const created = await create(name);
  if (created) {
    notify.success(`The sample ${name} was created.`);
  } else {
    notify.failure(`The sample ${name} could not be created.`);
  }
  return created;
}
</script>

<template>
  <main class="flex flex-col gap-6">
    <h1>Assessment</h1>
    <h2 v-if="permission === 'granted'">Manage</h2>
    <p v-if="permission === 'failed'" role="alert">Permissions for assessment could not be loaded.</p>
    <section aria-labelledby="assessment-samples" class="flex flex-col gap-4">
      <h2 id="assessment-samples">Samples</h2>
      <CreateAssessmentSampleForm ref="form" :on-create="onCreate" :saving="isSaving" />
      <p v-if="isError" role="alert">The samples could not be loaded.</p>
      <DataTable v-else :columns="columns" :data="rows ?? none" :loading="isLoading" :paging="page">
        <template #empty>
          <EmptyState>
            <template #icon><Inbox /></template>
            No samples yet.
            <template #action>
              <Button type="button" variant="outline" @click="form?.focus()">Create a sample</Button>
            </template>
          </EmptyState>
        </template>
      </DataTable>
    </section>
  </main>
</template>
