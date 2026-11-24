<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { cn } from '@/lib/utils';
import { type AssignmentStatus, statusLooks } from './assignmentStatus';

// An assignment's status as its icon and its word. The icon is decoration; the word says the status, so it is
// never shown by colour alone.
const props = defineProps<{ status: AssignmentStatus }>();

const { t } = useI18n();
const look = computed(() => statusLooks[props.status]);
</script>

<template>
  <span :class="cn('inline-flex items-center gap-1.5', look.tone)">
    <component :is="look.icon" aria-hidden="true" class="size-4 shrink-0" />
    <span>{{ t(`compliance.status.${status}`) }}</span>
  </span>
</template>
