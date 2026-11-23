<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import type { EditorialQuestionDto } from '@/api/generated';
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

interface RemoveQuestionDialogProps {
  /** The question to delete; none while the dialog is closed. */
  question?: EditorialQuestionDto;
  removing: boolean;
}

/** The most of a question's text the confirmation quotes. */
const quoted = 80;

// Asks before a question is deleted, naming it by the start of its text, as plain text (it may come from the model).
const { question, removing } = defineProps<RemoveQuestionDialogProps>();
const emit = defineEmits<{ confirm: [question: EditorialQuestionDto]; cancel: []; closed: [event: Event] }>();
const { t } = useI18n();

const named = computed(() => {
  const text = question?.text ?? '';
  return text.length > quoted ? `${text.slice(0, quoted)}…` : text;
});
</script>

<template>
  <Dialog :open="question !== undefined" @update:open="(open) => !open && emit('cancel')">
    <DialogContent @close-auto-focus="(event: Event) => emit('closed', event)">
      <DialogHeader>
        <DialogTitle>{{ t('editorial.test.questions.removeConfirm.title') }}</DialogTitle>
        <DialogDescription v-if="question !== undefined" class="whitespace-pre-line">
          {{ t('editorial.test.questions.removeConfirm.text', { text: named }) }}
        </DialogDescription>
      </DialogHeader>
      <DialogFooter>
        <DialogClose as-child>
          <Button type="button" variant="outline">{{ t('editorial.cancel') }}</Button>
        </DialogClose>
        <Button
          type="button"
          variant="destructive"
          :disabled="removing"
          @click="question !== undefined && emit('confirm', question)"
        >
          {{ t('editorial.test.questions.removeConfirm.confirm') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
