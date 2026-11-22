<script setup lang="ts">
import { computed } from 'vue';
import { renderMarkdown } from '@/lib/markdown';

interface MarkdownViewProps {
  /** The Markdown to show. */
  source: string;
  /** How far its headings sit below the page's: 2 makes a top-level heading an h3. */
  headingOffset?: number;
}

// The one component that puts HTML on the page, and only what renderMarkdown made of the Markdown: sanitised. The lint
// rule against raw HTML is off for this file alone (eslint.config.js); styled by .markdown in src/index.css.
const { source, headingOffset = 0 } = defineProps<MarkdownViewProps>();
const html = computed(() => renderMarkdown(source, { headingOffset }));
</script>

<template>
  <div class="markdown" v-html="html" />
</template>
