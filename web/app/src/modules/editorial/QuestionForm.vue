<script setup lang="ts">
import { Plus, X } from '@lucide/vue';
import { useFieldArray, useForm } from 'vee-validate';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import type { EditorialChapterDto, EditorialQuestionDto, EditorialQuestionRequest } from '@/api/generated';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { toTypedSchema } from '@/forms/toTypedSchema';
import type { MessageKey } from '@/i18n';
import { questionBounds, questionSchema } from './questionSchema';

interface QuestionFormProps {
  /** The version's chapters, one of which the question is about. */
  chapters: EditorialChapterDto[];
  /** The question to replace; none to add one. */
  question?: EditorialQuestionDto;
  /** Saves the question; resolves to whether it was saved. */
  onSave: (request: EditorialQuestionRequest) => Promise<boolean>;
  saving: boolean;
}

// Adds a question or replaces one: its chapter, its text, its type and 2 to 10 options, which of them are correct
// checked here as approval checks them (questionSchema), so a question saved through the form can be approved as it is.
// What saving answers, and what happens then, are the page's.
const { chapters, question, onSave, saving } = defineProps<QuestionFormProps>();
// Further actions beside the submit button, such as the dialog's Cancel.
defineSlots<{ actions?: () => unknown }>();

const types = ['SingleChoice', 'MultipleChoice'] as const;
const fields = {
  chapter: 'question-chapter',
  text: 'question-text',
  type: 'question-type',
  correct: 'question-correct',
};
const optionId = (index: number) => `question-option-${index}`;

/** The chapters by position, as the version lists them. */
const byPosition = computed(() => [...chapters].sort((a, b) => Number(a.position) - Number(b.position)));

const { defineField, errors, errorBag, handleSubmit } = useForm({
  validationSchema: toTypedSchema(questionSchema(chapters.map((chapter) => chapter.id))),
  initialValues: {
    chapterId: question?.chapterId ?? '',
    text: question?.text ?? '',
    type: question?.type ?? 'SingleChoice',
    options: question?.options.map((option) => ({ text: option.text, correct: option.correct })) ?? [
      { text: '', correct: false },
      { text: '', correct: false },
    ],
  },
});
const [chapterId, chapterAttributes] = defineField('chapterId');
const [text, textAttributes] = defineField('text');
const [type, typeAttributes] = defineField('type');
const { fields: options, push, remove, update } = useFieldArray<{ text: string; correct: boolean }>('options');
const { t, n } = useI18n();

// A question that switches to single choice keeps its first correct option only.
watch(type, (now) => {
  if (now !== 'SingleChoice') {
    return;
  }
  const first = options.value.findIndex((option) => option.value.correct);
  options.value.forEach((option, index) => {
    if (option.value.correct && index !== first) {
      update(index, { ...option.value, correct: false });
    }
  });
});

function setText(index: number, value: string | number) {
  const option = options.value[index];
  if (option !== undefined) {
    update(index, { ...option.value, text: String(value) });
  }
}

function setCorrect(index: number, correct: boolean) {
  options.value.forEach((option, each) => {
    if (each === index) {
      update(each, { ...option.value, correct });
    } else if (correct && type.value === 'SingleChoice' && option.value.correct) {
      update(each, { ...option.value, correct: false });
    }
  });
}

// The refusals show once a submit was refused, all of them at once, and follow the fields from then on.
const refused = ref(false);
const bounds = computed(() => ({
  min: n(questionBounds.options.min, 'integer'),
  max: n(questionBounds.options.max, 'integer'),
}));

/** A field's message, translated, once a submit was refused. */
function message(path: string, named: Record<string, string> = {}): string | undefined {
  const key = refused.value ? (errors.value as Record<string, string | undefined>)[path] : undefined;
  return key === undefined ? undefined : t(key as MessageKey, named);
}
/** The messages about the options as a whole: their number and which are correct. */
const optionsMessages = computed(() =>
  refused.value ? (errorBag.value.options ?? []).map((key) => t(key as MessageKey, bounds.value)) : [],
);
const textMessage = computed(() => message('text', { max: n(questionBounds.text.max, 'integer') }));
const optionMessage = (index: number) =>
  message(`options[${index}].text`, { max: n(questionBounds.option.max, 'integer') });

// A refused field takes the focus, the first one, so the editor corrects it where the refusal is read.
const submit = handleSubmit(
  async (values) => {
    await onSave(values);
  },
  ({ errors: found }) => {
    refused.value = true;
    const all = found as Record<string, string | undefined>;
    const first =
      all.chapterId !== undefined
        ? fields.chapter
        : all.text !== undefined
          ? fields.text
          : options.value.map((_, index) => index).find((index) => all[`options[${index}].text`] !== undefined);
    const target = typeof first === 'number' ? optionId(first) : first;
    if (target !== undefined) {
      document.getElementById(target)?.focus();
    }
  },
);
</script>

<template>
  <form novalidate class="flex flex-col gap-4" @submit="submit">
    <div class="flex flex-col gap-2">
      <label :for="fields.chapter" class="text-body font-medium">{{ t('editorial.test.questionForm.chapter') }}</label>
      <select
        :id="fields.chapter"
        v-model="chapterId"
        v-bind="chapterAttributes"
        aria-required="true"
        :aria-invalid="message('chapterId') ? true : undefined"
        :aria-describedby="message('chapterId') ? `${fields.chapter}-error` : undefined"
        class="border-input dark:bg-input/30 focus-visible:border-ring focus-visible:ring-ring/50 aria-invalid:border-destructive text-body h-9 w-full rounded-md border bg-transparent px-3 outline-none focus-visible:ring-3"
      >
        <option value="">{{ t('editorial.test.questionForm.chooseChapter') }}</option>
        <option v-for="chapter in byPosition" :key="chapter.id" :value="chapter.id">
          {{ `${t('editorial.chapters.position', { position: Number(chapter.position) })}: ${chapter.title}` }}
        </option>
      </select>
      <p v-if="message('chapterId')" :id="`${fields.chapter}-error`" role="alert" class="text-caption text-destructive">
        {{ message('chapterId') }}
      </p>
    </div>
    <div class="flex flex-col gap-2">
      <label :for="fields.text" class="text-body font-medium">{{ t('editorial.test.questionForm.text') }}</label>
      <textarea
        :id="fields.text"
        v-model="text"
        v-bind="textAttributes"
        rows="3"
        aria-required="true"
        :aria-invalid="textMessage ? true : undefined"
        :aria-describedby="textMessage ? `${fields.text}-error` : undefined"
        class="border-input dark:bg-input/30 focus-visible:border-ring focus-visible:ring-ring/50 aria-invalid:border-destructive text-body w-full rounded-md border bg-transparent px-3 py-2 outline-none focus-visible:ring-3"
      ></textarea>
      <p v-if="textMessage" :id="`${fields.text}-error`" role="alert" class="text-caption text-destructive">
        {{ textMessage }}
      </p>
    </div>
    <div class="flex flex-col gap-2">
      <span :id="fields.type" class="text-body font-medium">{{ t('editorial.test.questionForm.type') }}</span>
      <div role="radiogroup" :aria-labelledby="fields.type" class="flex flex-wrap gap-4">
        <label v-for="each in types" :key="each" class="text-body flex items-center gap-2">
          <input
            v-model="type"
            v-bind="typeAttributes"
            type="radio"
            name="question-type"
            :value="each"
            class="accent-primary"
          />
          {{ t(`glossary.questionType.${each}`) }}
        </label>
      </div>
    </div>
    <fieldset class="flex flex-col gap-3">
      <legend class="text-body mb-2 font-medium">{{ t('editorial.test.questionForm.options') }}</legend>
      <div v-for="(option, index) in options" :key="option.key" class="flex flex-col gap-1">
        <div class="flex items-center gap-2">
          <label :for="optionId(index)" class="text-body w-20 shrink-0 tabular-nums">{{
            t('editorial.test.questionForm.option', { number: index + 1 })
          }}</label>
          <Input
            :id="optionId(index)"
            :model-value="option.value.text"
            autocomplete="off"
            aria-required="true"
            :aria-invalid="optionMessage(index) ? true : undefined"
            :aria-describedby="optionMessage(index) ? `${optionId(index)}-error` : undefined"
            @update:model-value="(value) => setText(index, value)"
          />
          <label v-if="type === 'MultipleChoice'" class="text-caption flex shrink-0 items-center gap-1">
            <input
              type="checkbox"
              :checked="option.value.correct"
              class="accent-primary"
              :aria-label="t('editorial.test.questionForm.optionCorrect', { number: index + 1 })"
              @change="setCorrect(index, ($event.target as HTMLInputElement).checked)"
            />
            <span aria-hidden="true">{{ t('editorial.test.questions.correct') }}</span>
          </label>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            :disabled="options.length <= questionBounds.options.min"
            :aria-label="t('editorial.test.questionForm.removeOption', { number: index + 1 })"
            @click="remove(index)"
          >
            <X aria-hidden="true" />
          </Button>
        </div>
        <p
          v-if="optionMessage(index)"
          :id="`${optionId(index)}-error`"
          role="alert"
          class="text-caption text-destructive"
        >
          {{ optionMessage(index) }}
        </p>
      </div>
      <Button
        type="button"
        variant="outline"
        class="self-start"
        :disabled="options.length >= questionBounds.options.max"
        @click="push({ text: '', correct: false })"
      >
        <Plus aria-hidden="true" />{{ t('editorial.test.questionForm.addOption') }}
      </Button>
    </fieldset>
    <fieldset v-if="type === 'SingleChoice'" class="flex flex-col gap-2">
      <legend class="text-body mb-2 font-medium">{{ t('editorial.test.questionForm.correctOption') }}</legend>
      <div class="flex flex-wrap gap-4">
        <label v-for="(option, index) in options" :key="option.key" class="text-body flex items-center gap-2">
          <input
            type="radio"
            :name="fields.correct"
            :checked="option.value.correct"
            class="accent-primary"
            @change="setCorrect(index, true)"
          />
          {{ t('editorial.test.questionForm.option', { number: index + 1 }) }}
        </label>
      </div>
    </fieldset>
    <p v-for="each in optionsMessages" :key="each" role="alert" class="text-caption text-destructive">{{ each }}</p>
    <p v-if="question?.review === 'Approved'" class="text-caption bg-muted rounded-md p-3">
      {{ t('editorial.test.questionForm.reReview') }}
    </p>
    <div class="flex justify-end gap-2">
      <slot name="actions" />
      <Button type="submit" :disabled="saving">{{ t('editorial.test.questionForm.submit') }}</Button>
    </div>
  </form>
</template>
