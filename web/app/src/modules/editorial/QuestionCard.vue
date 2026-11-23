<script setup lang="ts">
import { Check, CircleAlert, CircleCheck, Pencil, Trash2 } from '@lucide/vue';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import type { EditorialChapterDto, EditorialQuestionDto } from '@/api/generated';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

interface QuestionCardProps {
  question: EditorialQuestionDto;
  /** The chapter the question is about; none when the version has no chapter of its id. */
  chapter?: EditorialChapterDto;
  /** Whether the question may be edited and deleted: in a draft only. */
  editable?: boolean;
  /** Whether an unreviewed question may be approved: in a draft or in review. */
  approvable?: boolean;
  /** Whether nothing may change for now, such as while the AI drafts the test. */
  locked?: boolean;
}

// One question of a test, as an item of the question list: its position, chapter and type, its text and options as
// plain text (they may come from the model: never markup), the correct options named, not marked by colour alone, and
// its review marked by a badge's text and icon.
const { question, chapter, editable = false, approvable = false, locked = false } = defineProps<QuestionCardProps>();
const emit = defineEmits<{
  edit: [question: EditorialQuestionDto];
  remove: [question: EditorialQuestionDto];
  approve: [question: EditorialQuestionDto];
}>();
const { t, d } = useI18n();

/**
 * Where the question came from and who approved it when (story E7): "KI-entworfen, geprüft von Emma Editor am
 * 20.11.2026". An approval stored before names were (or with a blank name) shows its day alone; one without a time
 * shows the origin alone. The day is the user's, in their time zone. The name is text, never markup.
 */
const provenance = computed(() => {
  const origin = t(`glossary.questionProvenance.${question.provenance}`);
  if (question.review === 'Unreviewed') {
    return t('editorial.test.questions.provenance.unreviewed', { origin });
  }
  if (question.approvedAt === null || question.approvedAt === undefined) {
    return origin;
  }
  const approvedOn = new Date(question.approvedAt);
  if (Number.isNaN(approvedOn.getTime())) {
    return origin;
  }
  const date = d(approvedOn, 'date');
  const name = question.approvedByName?.trim() ?? '';
  return name === ''
    ? t('editorial.test.questions.provenance.approvedUnnamed', { origin, date })
    : t('editorial.test.questions.provenance.approved', { origin, name, date });
});
</script>

<template>
  <li
    :id="`question-${question.id}`"
    tabindex="-1"
    :class="
      cn(
        'bg-card text-card-foreground shadow-elevation-1 flex flex-col gap-3 rounded-lg p-4',
        question.review === 'Unreviewed' && 'border-primary border-l-4',
      )
    "
  >
    <div class="text-caption text-muted-foreground flex flex-wrap items-center gap-x-3 gap-y-1">
      <span class="text-foreground font-medium tabular-nums">{{
        t('editorial.test.questions.position', { position: Number(question.position) })
      }}</span>
      <span>{{
        chapter === undefined
          ? t('editorial.test.questions.unknownChapter')
          : t('editorial.test.questions.chapter', { title: chapter.title })
      }}</span>
      <span>{{ t(`glossary.questionType.${question.type}`) }}</span>
      <span
        v-if="question.review === 'Approved'"
        class="bg-secondary text-secondary-foreground inline-flex items-center gap-1 rounded-md px-2 py-0.5"
      >
        <CircleCheck class="size-4" aria-hidden="true" />{{ t('glossary.questionReview.Approved') }}
      </span>
      <span v-else class="bg-primary text-primary-foreground inline-flex items-center gap-1 rounded-md px-2 py-0.5">
        <CircleAlert class="size-4" aria-hidden="true" />{{ t('glossary.questionReview.Unreviewed') }}
      </span>
    </div>
    <p class="text-body whitespace-pre-line">{{ question.text }}</p>
    <ul class="flex flex-col gap-1">
      <li v-for="option in question.options" :key="option.id" class="text-body flex items-start gap-2">
        <Check v-if="option.correct" class="text-primary mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <span v-else class="size-4 shrink-0" aria-hidden="true"></span>
        <span class="whitespace-pre-line">{{ option.text }}</span>
        <span class="sr-only">{{
          t(option.correct ? 'editorial.test.questions.correct' : 'editorial.test.questions.incorrect')
        }}</span>
      </li>
    </ul>
    <p class="text-caption text-muted-foreground">
      {{ provenance }}
    </p>
    <div v-if="editable || (approvable && question.review === 'Unreviewed')" class="flex flex-wrap justify-end gap-2">
      <Button
        v-if="approvable && question.review === 'Unreviewed'"
        :id="`question-${question.id}-approve`"
        type="button"
        size="sm"
        :disabled="locked"
        :aria-label="t('editorial.test.questions.approve', { position: Number(question.position) })"
        @click="emit('approve', question)"
      >
        <CircleCheck aria-hidden="true" />{{ t('editorial.test.questions.approveLabel') }}
      </Button>
      <template v-if="editable">
        <Button
          type="button"
          variant="outline"
          size="sm"
          :disabled="locked"
          :aria-label="t('editorial.test.questions.edit', { position: Number(question.position) })"
          @click="emit('edit', question)"
        >
          <Pencil aria-hidden="true" />
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          :disabled="locked"
          :aria-label="t('editorial.test.questions.remove', { position: Number(question.position) })"
          @click="emit('remove', question)"
        >
          <Trash2 aria-hidden="true" />
        </Button>
      </template>
    </div>
  </li>
</template>
