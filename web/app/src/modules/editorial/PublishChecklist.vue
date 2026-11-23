<script setup lang="ts">
import { Check, CircleAlert } from '@lucide/vue';
import { useI18n } from 'vue-i18n';
import type { ChecklistItem } from './publishChecklist';

interface PublishChecklistProps {
  items: ChecklistItem[];
}

// What blocks publishing a version, each item met or open. It is no live region: only the toasts announce.
const { items } = defineProps<PublishChecklistProps>();
const { t } = useI18n();
const headingId = 'publish-checklist-title';
</script>

<template>
  <div class="flex flex-col gap-2">
    <h3 :id="headingId" class="text-body font-medium">{{ t('editorial.workflow.checklist.title') }}</h3>
    <ul :aria-labelledby="headingId" class="flex flex-col gap-1">
      <li v-for="item in items" :key="item.name" class="text-body flex items-start gap-2">
        <Check v-if="item.met" aria-hidden="true" class="text-primary mt-0.5 size-4 shrink-0" />
        <CircleAlert v-else aria-hidden="true" class="text-muted-foreground mt-0.5 size-4 shrink-0" />
        <span>
          <span class="sr-only">{{
            `${t(item.met ? 'editorial.workflow.checklist.done' : 'editorial.workflow.checklist.open')} `
          }}</span>
          {{ item.values.count === undefined ? t(item.key, item.values) : t(item.key, item.values, item.values.count) }}
        </span>
      </li>
    </ul>
  </div>
</template>
