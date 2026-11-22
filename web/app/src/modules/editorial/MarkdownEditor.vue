<script setup lang="ts">
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';
import MarkdownView from '@/components/MarkdownView.vue';
import { Button } from '@/components/ui/button';

interface MarkdownEditorProps {
  /** The textarea's id, which its label names. */
  id: string;
  /** The error shown for the field, if any, by its element's id. */
  errorId?: string;
  invalid?: boolean;
}

// A chapter's Markdown: a textarea, and its preview rendered as a reader sees it (MarkdownView), behind two toggles.
// Its headings sit below the dialog's, as they sit below the page's.
defineProps<MarkdownEditorProps>();
const text = defineModel<string>({ required: true });
const previewing = ref(false);
const { t } = useI18n();
</script>

<template>
  <div class="flex flex-col gap-2">
    <div class="flex items-center justify-between gap-2">
      <label :for="id" class="text-body font-medium">{{ t('editorial.chapterForm.text') }}</label>
      <div class="flex gap-1">
        <Button
          type="button"
          size="sm"
          :variant="previewing ? 'ghost' : 'secondary'"
          :aria-pressed="!previewing"
          @click="previewing = false"
        >
          {{ t('editorial.chapterForm.write') }}
        </Button>
        <Button
          type="button"
          size="sm"
          :variant="previewing ? 'secondary' : 'ghost'"
          :aria-pressed="previewing"
          @click="previewing = true"
        >
          {{ t('editorial.chapterForm.preview') }}
        </Button>
      </div>
    </div>
    <textarea
      v-show="!previewing"
      :id="id"
      v-model="text"
      rows="12"
      aria-required="true"
      :aria-invalid="invalid ? true : undefined"
      :aria-describedby="invalid ? errorId : undefined"
      class="border-input dark:bg-input/30 focus-visible:border-ring focus-visible:ring-ring/50 aria-invalid:border-destructive min-h-40 w-full rounded-md border bg-transparent px-3 py-2 font-mono text-sm shadow-xs outline-none focus-visible:ring-3"
    />
    <div v-if="previewing" class="max-h-96 min-h-40 overflow-y-auto rounded-md border p-3">
      <MarkdownView v-if="text.trim() !== ''" :source="text" :heading-offset="2" />
      <p v-else class="text-body text-muted-foreground">{{ t('editorial.chapterForm.previewEmpty') }}</p>
    </div>
  </div>
</template>
