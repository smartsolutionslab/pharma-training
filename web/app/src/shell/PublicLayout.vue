<script setup lang="ts">
import { useI18n } from 'vue-i18n';
import { RouterView } from 'vue-router';
import LanguageSwitch from '@/shell/LanguageSwitch.vue';

// The layout of a public page (#54), which anyone opens, signed in or not: a header with the app's name and the
// language group above the page, and none of the shell, so nothing asks the host who the user is or for their
// pharmacies.
const { t } = useI18n();
</script>

<template>
  <div class="flex min-h-svh flex-col bg-background">
    <header class="flex h-12 shrink-0 items-center justify-between gap-2 border-b bg-glass px-4 backdrop-blur-md">
      <p class="font-semibold">{{ t('app.name') }}</p>
      <LanguageSwitch />
    </header>
    <RouterView />
  </div>
</template>
