<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, useTemplateRef } from 'vue';
import { useI18n } from 'vue-i18n';
import MarkdownView from '@/components/MarkdownView.vue';
import { shallowestHeading } from '@/lib/markdown';

interface TextChapterProps {
  /** The editor's Markdown, rendered sanitised by MarkdownView. */
  text: string;
  /** Whether the session has the chapter done: the hint to scroll to the end goes. */
  completed: boolean;
}

// A text chapter (#44) below the chapter's h2, so its headings start at h3: a `#` heading by an offset of 2, and a
// text whose top heading is deeper lifted to h3 too, so no level is skipped. It is done once its end has been in view
// (also at once, for a text that fits on the screen): an IntersectionObserver watches a marker after the text, and the
// chapter emits 100 a single time, never a percent in between.
const { text, completed } = defineProps<TextChapterProps>();
const emit = defineEmits<{ progress: [percent: number] }>();
const { t } = useI18n();
/** The chapter's h2 is level 2: the text's shallowest heading goes right below it. */
const headingOffset = computed(() => 3 - (shallowestHeading(text) ?? 1));

const end = useTemplateRef<HTMLElement>('end');
let observer: IntersectionObserver | undefined;

onMounted(() => {
  const marker = end.value;
  if (marker === null) {
    return;
  }
  observer = new IntersectionObserver((entries) => {
    if (observer !== undefined && entries.some((entry) => entry.isIntersecting)) {
      observer.disconnect();
      observer = undefined;
      emit('progress', 100);
    }
  });
  observer.observe(marker);
});

onBeforeUnmount(() => {
  observer?.disconnect();
  observer = undefined;
});
</script>

<template>
  <div class="flex flex-col gap-4">
    <p v-if="!completed" class="text-caption text-muted-foreground">{{ t('learning.player.text.hint') }}</p>
    <MarkdownView :source="text" :heading-offset="headingOffset" />
    <div ref="end" data-chapter-end aria-hidden="true" class="h-px" />
  </div>
</template>
