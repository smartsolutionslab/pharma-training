<script setup lang="ts">
import { ArrowDown, ArrowUp, GripVertical, Pencil, Trash2 } from '@lucide/vue';
import { useI18n } from 'vue-i18n';
import type { EditorialChapterDto } from '@/api/generated';
import MarkdownView from '@/components/MarkdownView.vue';
import { Button } from '@/components/ui/button';
import { formatDuration } from './formatDuration';

interface ChapterListProps {
  /** The chapters in the order to show them. */
  chapters: EditorialChapterDto[];
  /** A draft's chapters can be edited, deleted and moved; any other version's are shown read-only. */
  editable: boolean;
}

// A version's chapters as an ordered list: position, title, kind, a video's duration, whether a file was uploaded.
// Editable, each has its edit, delete and move buttons, named for it, and a handle to drag it by; read-only, a text
// chapter's Markdown opens below its title.
const { chapters, editable } = defineProps<ChapterListProps>();
const emit = defineEmits<{
  /** Every chapter's id in the new order. */
  reorder: [order: string[]];
  edit: [chapter: EditorialChapterDto];
  remove: [chapter: EditorialChapterDto];
}>();
const { t } = useI18n();

/** Moves the chapter at the index one place up (-1) or down (1). */
function move(index: number, by: -1 | 1) {
  const order = chapters.map((each) => each.id);
  const [moved] = order.splice(index, 1);
  if (moved === undefined) {
    return;
  }
  order.splice(index + by, 0, moved);
  emit('reorder', order);
}
</script>

<template>
  <ol :aria-label="t('editorial.chapters.title')" class="flex flex-col gap-2">
    <li
      v-for="(chapter, index) in chapters"
      :key="chapter.id"
      class="bg-card flex items-start gap-3 rounded-md border p-3"
    >
      <span
        v-if="editable"
        aria-hidden="true"
        :title="t('editorial.chapters.dragHint')"
        draggable="true"
        class="text-muted-foreground mt-1 cursor-grab"
      >
        <GripVertical class="size-4" />
      </span>
      <div class="flex min-w-0 flex-1 flex-col gap-1">
        <div class="text-caption text-muted-foreground flex flex-wrap gap-x-3 gap-y-1 tabular-nums">
          <span>{{ t('editorial.chapters.position', { position: index + 1 }) }}</span>
          <span>{{ t(`glossary.chapterKind.${chapter.kind}`) }}</span>
          <span v-if="chapter.kind === 'Video' && chapter.durationSeconds !== null">
            {{ t('editorial.chapters.duration', { duration: formatDuration(chapter.durationSeconds) }) }}
          </span>
          <span v-if="chapter.kind !== 'Text' && chapter.file !== null">{{ t('editorial.chapters.file') }}</span>
        </div>
        <details v-if="!editable && chapter.kind === 'Text' && chapter.text !== null">
          <summary class="text-body cursor-pointer font-medium">{{ chapter.title }}</summary>
          <MarkdownView class="pt-2" :source="chapter.text" :heading-offset="2" />
        </details>
        <span v-else class="text-body font-medium">{{ chapter.title }}</span>
      </div>
      <div v-if="editable" class="flex shrink-0 gap-1">
        <Button
          type="button"
          variant="ghost"
          size="icon"
          :aria-label="t('editorial.chapters.moveUp', { title: chapter.title })"
          :disabled="index === 0"
          @click="move(index, -1)"
        >
          <ArrowUp aria-hidden="true" />
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          :aria-label="t('editorial.chapters.moveDown', { title: chapter.title })"
          :disabled="index === chapters.length - 1"
          @click="move(index, 1)"
        >
          <ArrowDown aria-hidden="true" />
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          :aria-label="t('editorial.chapters.edit', { title: chapter.title })"
          @click="emit('edit', chapter)"
        >
          <Pencil aria-hidden="true" />
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          :aria-label="t('editorial.chapters.remove', { title: chapter.title })"
          @click="emit('remove', chapter)"
        >
          <Trash2 aria-hidden="true" />
        </Button>
      </div>
    </li>
  </ol>
</template>
