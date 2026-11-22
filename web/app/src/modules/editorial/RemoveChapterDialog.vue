<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import type { EditorialChapterDto, EditorialQuestionDto } from '@/api/generated';
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

interface RemoveChapterDialogProps {
  /** The chapter to delete; none while the dialog is closed. */
  chapter?: EditorialChapterDto;
  /** The version's questions: those about the chapter are deleted with it. */
  questions: EditorialQuestionDto[];
  removing: boolean;
}

// Asks before a chapter is deleted, naming it and how many questions go with it.
const { chapter, questions, removing } = defineProps<RemoveChapterDialogProps>();
const emit = defineEmits<{ confirm: [chapter: EditorialChapterDto]; cancel: [] }>();
const { t } = useI18n();

const asked = computed(() => questions.filter((each) => each.chapterId === chapter?.id).length);
</script>

<template>
  <Dialog :open="chapter !== undefined" @update:open="(open) => !open && emit('cancel')">
    <DialogContent>
      <DialogHeader>
        <DialogTitle>{{ t('editorial.chapters.removeConfirm.title') }}</DialogTitle>
        <DialogDescription v-if="chapter !== undefined" class="flex flex-col gap-2">
          <span>{{ t('editorial.chapters.removeConfirm.text', { title: chapter.title }) }}</span>
          <span v-if="asked > 0">{{ t('editorial.chapters.removeConfirm.questions', { count: asked }, asked) }}</span>
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
          @click="chapter !== undefined && emit('confirm', chapter)"
        >
          {{ t('editorial.chapters.removeConfirm.confirm') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
