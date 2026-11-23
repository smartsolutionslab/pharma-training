<script setup lang="ts">
import { useI18n } from 'vue-i18n';

interface MembershipSelectProps {
  /** The select's id, which its label and its message are tied to. */
  id: string;
  label: string;
  /** The values offered, each with its label. */
  options: readonly { value: string; label: string }[];
  /** The first option, without a value, while nothing is chosen; none where something always is. */
  placeholder?: string;
  /** The field's message, a catalog key; none while the field is valid. */
  error?: string;
}

// A select of a job function or a role, with its label and its message beside it; further attributes (vee-validate's
// field attributes) go to the select.
defineOptions({ inheritAttrs: false });
const { id, label, options, placeholder, error } = defineProps<MembershipSelectProps>();
const value = defineModel<string>();
const { t } = useI18n();
</script>

<template>
  <div class="flex flex-col gap-2">
    <label :for="id" class="text-body font-medium">{{ label }}</label>
    <select
      :id="id"
      v-model="value"
      v-bind="$attrs"
      aria-required="true"
      :aria-invalid="error ? true : undefined"
      :aria-describedby="error ? `${id}-error` : undefined"
      class="h-9 w-full min-w-0 rounded-md border border-input bg-transparent px-3 py-1 text-body motion-safe:transition-[color,box-shadow] outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-destructive/20 dark:bg-input/30 dark:aria-invalid:ring-destructive/40 [&_option]:bg-popover [&_option]:text-popover-foreground"
    >
      <option v-if="placeholder !== undefined" value="" disabled>{{ placeholder }}</option>
      <option v-for="option in options" :key="option.value" :value="option.value">{{ option.label }}</option>
    </select>
    <p v-if="error" :id="`${id}-error`" role="alert" class="text-caption text-destructive">{{ t(error) }}</p>
  </div>
</template>
