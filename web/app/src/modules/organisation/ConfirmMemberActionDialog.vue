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

interface ConfirmMemberActionDialogProps {
  open: boolean;
  /** The question, the description of what follows and the confirmation's label, in the language shown. */
  title: string;
  description: string;
  confirmLabel: string;
  /** Whether the action is on its way to the host: the confirmation stays disabled until it has answered. */
  pending: boolean;
}

// Asks before an invitation is revoked or a membership ended; both are destructive. It opens with the focus on Cancel.
const { open, title, description, confirmLabel, pending } = defineProps<ConfirmMemberActionDialogProps>();
const emit = defineEmits<{ confirm: []; cancel: []; closed: [event: Event] }>();
const { t } = useI18n();
</script>

<template>
  <Dialog :open="open" @update:open="(shown) => !shown && !pending && emit('cancel')">
    <DialogContent :close-disabled="pending" @close-auto-focus="(event: Event) => emit('closed', event)">
      <DialogHeader>
        <DialogTitle>{{ title }}</DialogTitle>
        <DialogDescription>{{ description }}</DialogDescription>
      </DialogHeader>
      <DialogFooter>
        <DialogClose as-child>
          <Button type="button" variant="outline" :disabled="pending">{{ t('organisation.team.cancel') }}</Button>
        </DialogClose>
        <Button type="button" variant="destructive" :disabled="pending" @click="emit('confirm')">
          {{ confirmLabel }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
