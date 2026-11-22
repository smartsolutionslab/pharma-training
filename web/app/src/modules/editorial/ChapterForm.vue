<script setup lang="ts">
import { useForm } from 'vee-validate';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import type { EditorialChapterDto, EditorialChapterRequest, EditorialQuestionDto } from '@/api/generated';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { toTypedSchema } from '@/forms/toTypedSchema';
import { chapterSchema, maxTextLength } from './chapterSchema';
import MarkdownEditor from './MarkdownEditor.vue';

interface ChapterFormProps {
  /** The chapter to replace; none to add one. */
  chapter?: EditorialChapterDto;
  /** The version's questions: replacing a chapter makes its approved ones need reviewing again. */
  questions: EditorialQuestionDto[];
  /** Saves the chapter; resolves to whether it was saved. */
  onSave: (request: EditorialChapterRequest) => Promise<boolean>;
  saving: boolean;
}

// Adds a chapter or replaces one: its title, its kind and, for a text chapter, its Markdown, checked here as the host
// checks them (chapterSchema). What saving answers, and what happens then, are the page's.
const { chapter, questions, onSave, saving } = defineProps<ChapterFormProps>();
// Further actions beside the submit button, such as the dialog's Cancel.
defineSlots<{ actions?: () => unknown }>();

const kinds = ['Text', 'Pdf', 'Video'] as const;
const fields = { title: 'chapter-title', kind: 'chapter-kind', text: 'chapter-text' };

const {
  defineField,
  errors: checked,
  handleSubmit,
  values,
} = useForm({
  validationSchema: toTypedSchema(chapterSchema(chapter)),
  initialValues: { title: chapter?.title ?? '', kind: chapter?.kind ?? 'Text', text: chapter?.text ?? '' },
});
const [title, titleAttributes] = defineField('title');
const [kind, kindAttributes] = defineField('kind');
const [text] = defineField('text');
const { t, n } = useI18n();

// The refusals show once a submit was refused, all of them at once, and follow the fields from then on: nothing is
// refused while the editor is still writing.
const refused = ref(false);
const errors = computed(() => (refused.value ? checked.value : {}));

/** The chapter's approved questions, which need reviewing again once it is replaced. */
const approved = computed(
  () => questions.filter((each) => each.chapterId === chapter?.id && each.review === 'Approved').length,
);
const keepsFile = computed(() => chapter !== undefined && values.kind !== 'Text' && chapter.kind === values.kind);

// A refused field takes the focus, the first one, so the editor corrects it where the refusal is read.
const submit = handleSubmit(
  async (request) => {
    await onSave(request);
  },
  ({ errors: found }) => {
    refused.value = true;
    const first = (['title', 'text'] as const).find((field) => found[field] !== undefined);
    if (first !== undefined) {
      document.getElementById(fields[first])?.focus();
    }
  },
);
</script>

<template>
  <form novalidate class="flex flex-col gap-4" @submit="submit">
    <div class="flex flex-col gap-2">
      <label :for="fields.title" class="text-body font-medium">{{ t('editorial.chapterForm.title') }}</label>
      <Input
        :id="fields.title"
        v-model="title"
        v-bind="titleAttributes"
        autocomplete="off"
        aria-required="true"
        :aria-invalid="errors.title ? true : undefined"
        :aria-describedby="errors.title ? `${fields.title}-error` : undefined"
      />
      <p v-if="errors.title" :id="`${fields.title}-error`" role="alert" class="text-caption text-destructive">
        {{ t(errors.title) }}
      </p>
    </div>
    <div class="flex flex-col gap-2">
      <span :id="fields.kind" class="text-body font-medium">{{ t('editorial.chapterForm.kind') }}</span>
      <div
        role="radiogroup"
        :aria-labelledby="fields.kind"
        :aria-describedby="errors.kind ? `${fields.kind}-error` : undefined"
        class="flex flex-wrap gap-4"
      >
        <label v-for="each in kinds" :key="each" class="text-body flex items-center gap-2">
          <input v-model="kind" v-bind="kindAttributes" type="radio" name="kind" :value="each" class="accent-primary" />
          {{ t(`glossary.chapterKind.${each}`) }}
        </label>
      </div>
    </div>
    <template v-if="kind === 'Text'">
      <MarkdownEditor
        :id="fields.text"
        v-model="text"
        :invalid="errors.text !== undefined"
        :error-id="`${fields.text}-error`"
      />
      <p v-if="errors.text" :id="`${fields.text}-error`" role="alert" class="text-caption text-destructive">
        {{ t(errors.text, { max: n(maxTextLength, 'integer') }) }}
      </p>
    </template>
    <template v-else>
      <p v-if="keepsFile" class="text-caption text-muted-foreground">{{ t('editorial.chapterForm.fileKept') }}</p>
      <p v-if="errors.kind" :id="`${fields.kind}-error`" role="alert" class="text-caption text-destructive">
        {{ t(errors.kind) }}
      </p>
    </template>
    <p v-if="approved > 0" class="text-caption bg-muted rounded-md p-3">
      {{ t('editorial.chapterForm.requalify', { count: approved }, approved) }}
    </p>
    <div class="flex justify-end gap-2">
      <slot name="actions" />
      <Button type="submit" :disabled="saving">{{ t('editorial.chapterForm.submit') }}</Button>
    </div>
  </form>
</template>
