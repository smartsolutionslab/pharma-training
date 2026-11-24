<script setup lang="ts">
import { useI18n } from 'vue-i18n';
import { Button } from '@/components/ui/button';
import { type Language, languages, type MessageKey, setLanguage } from '@/i18n';

// The language group: one button per language, the current one pressed. The user menu and the public header show it.
const { t, locale } = useI18n();

// Each language is named in its own words, the same in both catalogs.
const labels: Record<Language, MessageKey> = { 'de-DE': 'language.de', 'en-GB': 'language.en' };
</script>

<template>
  <div role="group" :aria-label="t('language.label')" class="flex gap-1">
    <Button
      v-for="language in languages"
      :key="language"
      :variant="language === locale ? 'secondary' : 'ghost'"
      size="sm"
      :lang="language"
      :aria-pressed="language === locale"
      @click="setLanguage(language)"
    >
      {{ t(labels[language]) }}
    </Button>
  </div>
</template>
