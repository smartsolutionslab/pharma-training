<script setup lang="ts">
import { CircleCheck, CircleX } from '@lucide/vue';
import { useI18n } from 'vue-i18n';
import type { CertificateValidity } from '@/api/generated';

// Whether a certificate is valid or expired: the word, which is text, and an icon beside it that is decoration only.
defineProps<{ validity: CertificateValidity }>();
const { t } = useI18n();
</script>

<template>
  <span class="inline-flex items-center gap-2">
    <CircleCheck v-if="validity === 'Valid'" aria-hidden="true" class="size-5 shrink-0 text-primary" />
    <CircleX v-else aria-hidden="true" class="size-5 shrink-0 text-destructive" />
    <span>{{ t(`certification.validity.${validity}`) }}</span>
  </span>
</template>
