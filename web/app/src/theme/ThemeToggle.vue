<script setup lang="ts">
import { ref, watchEffect } from 'vue';
import { useI18n } from 'vue-i18n';
import type { MessageKey } from '@/i18n';
import { Button } from '@/components/ui/button';
import { applyTheme, onSystemThemeChange, storedTheme, storeTheme, type Theme, themes } from './theme';

// Switches the app between the light theme, the dark one and the system's, and remembers the choice.
const labels: Record<Theme, MessageKey> = { light: 'theme.light', dark: 'theme.dark', system: 'theme.system' };
const { t } = useI18n();
const theme = ref<Theme>(storedTheme());

// Synchronously, so a choice shows at once.
watchEffect(
  (onCleanup) => {
    applyTheme(theme.value);
    if (theme.value === 'system') {
      onCleanup(onSystemThemeChange(() => applyTheme('system')));
    }
  },
  { flush: 'sync' },
);

function choose(option: Theme) {
  storeTheme(option);
  theme.value = option;
}
</script>

<template>
  <div role="group" :aria-label="t('theme.label')" class="ml-auto flex gap-1">
    <Button
      v-for="option in themes"
      :key="option"
      :variant="option === theme ? 'secondary' : 'ghost'"
      size="sm"
      :aria-pressed="option === theme"
      @click="choose(option)"
    >
      {{ t(labels[option]) }}
    </Button>
  </div>
</template>
