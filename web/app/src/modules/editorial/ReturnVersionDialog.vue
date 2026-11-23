<script setup lang="ts">
import { useForm } from 'vee-validate';
import { nextTick, useId, useTemplateRef, watch } from 'vue';
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
import { toTypedSchema } from '@/forms/toTypedSchema';
import { returnReasonMaxLength, returnReasonSchema } from './returnReasonSchema';

interface ReturnVersionDialogProps {
  open: boolean;
  /** Whether the return is on its way to the host: the confirmation stays disabled until it has answered. */
  pending: boolean;
  /**
   * Returns the version with the reason, trimmed. Answers reasonRefused when the host refused the reason itself: the
   * dialog stays open with the field's message; tryAgain when the page keeps it open for the editor to send the reason
   * again; done when whatever follows is the page's.
   */
  onReturn: (reason: string) => Promise<'done' | 'reasonRefused' | 'tryAgain'>;
}

// Asks for the reason a version in review goes back to draft (ADR 0013), with the hint not to name people.
const { open, pending, onReturn } = defineProps<ReturnVersionDialogProps>();
const emit = defineEmits<{ cancel: []; closed: [event: Event] }>();
const { t } = useI18n();

const reasonId = useId();
const hintId = `${reasonId}-hint`;
const errorId = `${reasonId}-error`;
const textarea = useTemplateRef<HTMLTextAreaElement>('textarea');

const { defineField, errors, handleSubmit, resetForm, setFieldError } = useForm({
  validationSchema: toTypedSchema(returnReasonSchema),
  initialValues: { reason: '' },
});
const [reason, reasonAttributes] = defineField('reason');

// Each time the dialog opens, it asks afresh.
watch(
  () => open,
  (opened) => {
    if (opened) {
      resetForm();
    }
  },
);

const submit = handleSubmit(async (values) => {
  const outcome = await onReturn(values.reason);
  if (outcome === 'done') {
    return;
  }
  if (outcome === 'reasonRefused') {
    setFieldError('reason', 'editorial.workflow.returnForm.reasonInvalid');
  }
  await nextTick();
  textarea.value?.focus();
});
</script>

<template>
  <Dialog :open="open" @update:open="(opened) => !opened && !pending && emit('cancel')">
    <DialogContent :close-disabled="pending" @close-auto-focus="(event: Event) => emit('closed', event)">
      <DialogHeader>
        <DialogTitle>{{ t('editorial.workflow.returnForm.title') }}</DialogTitle>
        <DialogDescription>{{ t('editorial.workflow.returnForm.description') }}</DialogDescription>
      </DialogHeader>
      <form class="flex flex-col gap-4" novalidate @submit.prevent="submit">
        <div class="flex flex-col gap-2">
          <label :for="reasonId" class="text-body font-medium">{{ t('editorial.workflow.returnForm.reason') }}</label>
          <textarea
            :id="reasonId"
            ref="textarea"
            v-model="reason"
            v-bind="reasonAttributes"
            rows="4"
            aria-required="true"
            :aria-invalid="errors.reason ? true : undefined"
            :aria-describedby="errors.reason ? `${hintId} ${errorId}` : hintId"
            class="border-input dark:bg-input/30 focus-visible:border-ring focus-visible:ring-ring/50 aria-invalid:border-destructive text-body w-full rounded-md border bg-transparent px-3 py-2 outline-none focus-visible:ring-3"
          ></textarea>
          <p :id="hintId" class="text-caption text-muted-foreground">{{ t('editorial.workflow.returnForm.hint') }}</p>
          <p v-if="errors.reason" :id="errorId" role="alert" class="text-caption text-destructive">
            {{ t(errors.reason, { max: returnReasonMaxLength }) }}
          </p>
        </div>
        <DialogFooter>
          <DialogClose as-child>
            <Button type="button" variant="outline" :disabled="pending">{{ t('editorial.cancel') }}</Button>
          </DialogClose>
          <Button type="submit" :disabled="pending">{{ t('editorial.workflow.returnForm.confirm') }}</Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>
