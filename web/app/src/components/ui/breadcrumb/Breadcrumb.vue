<script lang="ts" setup>
import type { HTMLAttributes } from 'vue';
import { useI18n } from 'vue-i18n';

// shadcn-vue names the file in one word; lint wants a component's name in two.
defineOptions({ name: 'UiBreadcrumb' });

const props = defineProps<{
  class?: HTMLAttributes['class'];
}>();

const { t } = useI18n();
</script>

<template>
  <nav :aria-label="t('ui.breadcrumb.label')" data-slot="breadcrumb" :class="props.class">
    <slot />
  </nav>
</template>
