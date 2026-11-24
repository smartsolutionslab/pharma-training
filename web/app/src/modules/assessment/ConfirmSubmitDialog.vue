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

interface ConfirmSubmitDialogProps {
  open: boolean;
  /** How many questions have an answer, and how many there are. */
  answered: number;
  total: number;
  /** Whether the submit is on its way: the dialog stays open and its buttons disabled until the host has answered. */
  pending: boolean;
}

// Asks before the test is submitted (#48): how many questions are answered, that answers cannot be changed afterwards,
// and, with some unanswered, that those count as wrong. "Weiter bearbeiten" comes first and takes the focus.
const { open, answered, total, pending } = defineProps<ConfirmSubmitDialogProps>();
const emit = defineEmits<{ confirm: []; cancel: [] }>();
const { t } = useI18n();
</script>

<template>
  <Dialog :open="open" @update:open="(opened) => !opened && !pending && emit('cancel')">
    <DialogContent :close-disabled="pending">
      <DialogHeader>
        <DialogTitle>{{ t('assessment.test.confirm.title') }}</DialogTitle>
        <DialogDescription>
          {{ t('assessment.test.confirm.text', { answered, total }) }}
          <template v-if="answered < total"> {{ t('assessment.test.confirm.unanswered') }}</template>
        </DialogDescription>
      </DialogHeader>
      <DialogFooter>
        <DialogClose as-child>
          <Button type="button" variant="outline" :disabled="pending">{{ t('assessment.test.confirm.cancel') }}</Button>
        </DialogClose>
        <Button type="button" :disabled="pending" @click="!pending && emit('confirm')">
          {{ t('assessment.test.confirm.confirm') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
