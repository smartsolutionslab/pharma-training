<script setup lang="ts">
import { TriangleAlert } from '@lucide/vue';
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

interface TestDraftDialogProps {
  open: boolean;
  /** The questions the AI drafts: the blueprint less the approved ones. */
  toDraft: number;
  /** The unreviewed questions the draft replaces, hand-written ones included (the owner's decision, ADR 0008). */
  unreviewed: number;
  /** Whether the version has PDF or video chapters, which the AI does not read. */
  hasFileChapters: boolean;
}

// Asks before the AI drafts the test: how many questions it drafts, and how many unreviewed questions it replaces.
const { open, toDraft, unreviewed, hasFileChapters } = defineProps<TestDraftDialogProps>();
const emit = defineEmits<{ confirm: []; cancel: []; closed: [event: Event] }>();
const { t, n } = useI18n();

/** Whether the draft was asked for since the dialog opened: one press asks once, a second one is refused. */
const pressed = ref(false);
watch(
  () => open,
  (now) => {
    if (now) {
      pressed.value = false;
    }
  },
);

function confirm() {
  if (pressed.value) {
    return;
  }
  pressed.value = true;
  emit('confirm');
}
</script>

<template>
  <Dialog :open="open" @update:open="(now) => !now && emit('cancel')">
    <DialogContent @close-auto-focus="(event: Event) => emit('closed', event)">
      <DialogHeader>
        <DialogTitle>{{ t('editorial.test.draft.confirm.title') }}</DialogTitle>
        <DialogDescription class="flex flex-col gap-3">
          <span>{{
            t('editorial.test.draft.confirm.requested', { count: n(toDraft, 'integer') }, Math.max(toDraft, 0))
          }}</span>
          <span v-if="unreviewed > 0" class="text-foreground bg-muted flex items-start gap-2 rounded-md p-3">
            <TriangleAlert class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
            <span>{{
              t('editorial.test.draft.confirm.replaces', { count: n(unreviewed, 'integer') }, unreviewed)
            }}</span>
          </span>
          <span v-if="hasFileChapters">{{ t('editorial.test.draft.confirm.leftOut') }}</span>
        </DialogDescription>
      </DialogHeader>
      <DialogFooter>
        <DialogClose as-child>
          <Button type="button" variant="outline">{{ t('editorial.cancel') }}</Button>
        </DialogClose>
        <Button type="button" :disabled="pressed" @click="confirm">{{
          t('editorial.test.draft.confirm.submit')
        }}</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
