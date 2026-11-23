<script setup lang="ts">
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';

interface ConfirmWorkflowDialogProps {
  /** The step asked about; none while the dialog is closed. */
  step?: 'publish' | 'retire';
  /** The version's number and title, which the question names. */
  number: number;
  title: string;
  /** Whether the step is on its way to the host: the confirmation stays disabled until it has answered. */
  pending: boolean;
}

// Asks before a version is published or retired, naming it and what follows; retiring is destructive.
const { step, number, title, pending } = defineProps<ConfirmWorkflowDialogProps>();
const emit = defineEmits<{ confirm: [step: 'publish' | 'retire']; cancel: []; closed: [event: Event] }>();
const { t } = useI18n();

/** The step last asked about: the dialog keeps its words while it animates out. */
const shown = ref(step);
watch(
  () => step,
  (asked) => {
    if (asked !== undefined) {
      shown.value = asked;
    }
  },
);
</script>

<template>
  <Dialog :open="step !== undefined" @update:open="(open) => !open && !pending && emit('cancel')">
    <DialogContent :close-disabled="pending" @close-auto-focus="(event: Event) => emit('closed', event)">
      <template v-if="shown !== undefined">
        <DialogHeader>
          <DialogTitle>{{ t(`editorial.workflow.${shown}Confirm.title`) }}</DialogTitle>
          <DialogDescription>{{ t(`editorial.workflow.${shown}Confirm.text`, { number, title }) }}</DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <DialogClose as-child>
            <Button type="button" variant="outline" :disabled="pending">{{ t('editorial.cancel') }}</Button>
          </DialogClose>
          <Button
            type="button"
            :variant="shown === 'retire' ? 'destructive' : 'default'"
            :disabled="pending"
            @click="step !== undefined && emit('confirm', step)"
          >
            {{ t(`editorial.workflow.${shown}Confirm.confirm`) }}
          </Button>
        </DialogFooter>
      </template>
    </DialogContent>
  </Dialog>
</template>
