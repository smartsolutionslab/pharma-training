<script setup lang="ts">
import { useI18n } from 'vue-i18n';

// Where a course version opens (/editorial/versions/:id) until its page is built (issue #24, T6): its title only.
const { t } = useI18n();
</script>

<template>
  <main class="flex flex-col gap-6">
    <h1 class="text-title">{{ t('modules.editorial.details.version.title') }}</h1>
  </main>
</template>
