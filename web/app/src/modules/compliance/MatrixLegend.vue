<script setup lang="ts">
import { useI18n } from 'vue-i18n';
import AssignmentStatusLabel from '@/components/status/AssignmentStatusLabel.vue';
import { statusOrder } from '@/components/status/assignmentStatus';

// The matrix's key: every status with its icon and its word, as the cells show them.
const { t } = useI18n();
</script>

<template>
  <ul :aria-label="t('compliance.matrix.table.legend')" class="text-caption flex flex-wrap gap-x-4 gap-y-2">
    <li v-for="status in statusOrder" :key="status">
      <AssignmentStatusLabel :status="status" />
    </li>
  </ul>
</template>
