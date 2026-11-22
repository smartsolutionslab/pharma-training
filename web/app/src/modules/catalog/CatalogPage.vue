<script setup lang="ts">
import { Inbox } from '@lucide/vue';
import { useTemplateRef } from 'vue';
import { useI18n } from 'vue-i18n';
import type { CatalogSampleDto } from '@/api/generated';
import { Button } from '@/components/ui/button';
import DataTable from '@/components/ui/DataTable.vue';
import { columnsOf, translatedHeader } from '@/components/ui/data-table-columns';
import { EmptyState } from '@/components/ui/empty-state';
import { t as translate } from '@/i18n';
import { notify } from '@/lib/notify';
import CreateCatalogSampleForm from './CreateCatalogSampleForm.vue';
import { titleKey } from './titleKey';
import { useCanManage } from './useCanManage';
import { useCreateCatalogSample, useCatalogSamples } from './useCatalogSamples';

// The module's screen, starting from its sample (AGENTS.md): the samples in a table, by name, and a form that creates
// one, shown in the table at once and taken out again if the host refuses it; the result is a toast. "Manage"
// shows only to a caller its permission probe answers with 200, and any failure but a 403 shows as an alert.
const samples = columnsOf<CatalogSampleDto>();
const columns = samples.columns([samples.accessor('name', translatedHeader('samples.form.name'))]);
const none: CatalogSampleDto[] = [];

const { samples: rows, page, isLoading, isError } = useCatalogSamples();
const { create, isSaving } = useCreateCatalogSample();
const permission = useCanManage();
const form = useTemplateRef('form');
const { t } = useI18n();

async function onCreate(name: string) {
  const created = await create(name);
  if (created) {
    notify.success(translate('samples.created', { name }));
  } else {
    notify.failure(translate('samples.createFailed', { name }));
  }
  return created;
}
</script>

<template>
  <main class="flex flex-col gap-6">
    <h1>{{ t(titleKey) }}</h1>
    <h2 v-if="permission === 'granted'">{{ t('samples.manage') }}</h2>
    <p v-if="permission === 'failed'" role="alert">
      {{ t('samples.permissionsFailed', { module: t(titleKey) }) }}
    </p>
    <section aria-labelledby="catalog-samples" class="flex flex-col gap-4">
      <h2 id="catalog-samples">{{ t('samples.title') }}</h2>
      <CreateCatalogSampleForm ref="form" :on-create="onCreate" :saving="isSaving" />
      <p v-if="isError" role="alert">{{ t('samples.loadFailed') }}</p>
      <DataTable v-else :columns="columns" :data="rows ?? none" :loading="isLoading" :paging="page">
        <template #empty>
          <EmptyState>
            <template #icon><Inbox /></template>
            {{ t('samples.empty') }}
            <template #action>
              <Button type="button" variant="outline" @click="form?.focus()">{{ t('samples.createFirst') }}</Button>
            </template>
          </EmptyState>
        </template>
      </DataTable>
    </section>
  </main>
</template>
