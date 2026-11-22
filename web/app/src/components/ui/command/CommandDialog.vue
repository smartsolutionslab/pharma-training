<script setup lang="ts">
import type { DialogRootEmits, DialogRootProps } from 'reka-ui';
import { useForwardPropsEmits } from 'reka-ui';
import { useI18n } from 'vue-i18n';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import Command from './Command.vue';

// Without a title or description of its own, the dialog is named and described from the catalogs (ui.command.*).
const props = defineProps<
  DialogRootProps & {
    title?: string;
    description?: string;
  }
>();
const emits = defineEmits<DialogRootEmits>();

const forwarded = useForwardPropsEmits(props, emits);
const { t } = useI18n();
</script>

<template>
  <Dialog v-slot="slotProps" v-bind="forwarded">
    <DialogContent class="overflow-hidden p-0">
      <DialogHeader class="sr-only">
        <DialogTitle>{{ title ?? t('ui.command.title') }}</DialogTitle>
        <DialogDescription>{{ description ?? t('ui.command.description') }}</DialogDescription>
      </DialogHeader>
      <Command class="bg-transparent">
        <slot v-bind="slotProps" />
      </Command>
    </DialogContent>
  </Dialog>
</template>
