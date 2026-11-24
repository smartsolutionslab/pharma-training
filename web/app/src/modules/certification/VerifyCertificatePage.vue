<script setup lang="ts">
import { useI18n } from 'vue-i18n';

// Stub for #54 (T2, red first): the public verification page renders its heading only.
const { t } = useI18n();
</script>

<template>
  <main class="mx-auto flex w-full max-w-md flex-col gap-6 px-4 py-6">
    <h1 class="text-title">{{ t('modules.certification.entries.verify.title') }}</h1>
  </main>
</template>
