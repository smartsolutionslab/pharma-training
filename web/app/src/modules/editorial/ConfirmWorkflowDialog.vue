<script setup lang="ts">
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
</script>

<template>
  <Dialog :open="step !== undefined" @update:open="(open) => !open && !pending && emit('cancel')">
    <DialogContent :close-disabled="pending" @close-auto-focus="(event: Event) => emit('closed', event)">
      <template v-if="step !== undefined">
        <DialogHeader>
          <DialogTitle>{{ t(`editorial.workflow.${step}Confirm.title`) }}</DialogTitle>
          <DialogDescription>{{ t(`editorial.workflow.${step}Confirm.text`, { number, title }) }}</DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <DialogClose as-child>
            <Button type="button" variant="outline" :disabled="pending">{{ t('editorial.cancel') }}</Button>
          </DialogClose>
          <Button
            type="button"
            :variant="step === 'retire' ? 'destructive' : 'default'"
            :disabled="pending"
            @click="emit('confirm', step)"
          >
            {{ t(`editorial.workflow.${step}Confirm.confirm`) }}
          </Button>
        </DialogFooter>
      </template>
    </DialogContent>
  </Dialog>
</template>
