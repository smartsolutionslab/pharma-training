<script setup lang="ts">
import { useForm } from 'vee-validate';
import { useI18n } from 'vue-i18n';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { toTypedSchema } from '@/forms/toTypedSchema';
import type { MessageKey } from '@/i18n';
import { courseTitleMaxLength } from './courseTitle';

interface CreateCourseFormProps {
  /** Starts the course; resolves to whether it was started. */
  onCreate: (title: string) => Promise<boolean>;
  saving: boolean;
}

// Starts a course: its title, checked here as the host checks it (CourseTitle: 1 to 150 characters, trimmed, no NUL)
// before the host checks it again.
const { onCreate, saving } = defineProps<CreateCourseFormProps>();
// Further actions beside the submit button, such as the dialog's Cancel.
defineSlots<{ actions?: () => unknown }>();

const titleField = 'course-title';

// Each message is a key, rendered with t where it is shown: a message already shown follows a switch of the language.
const courseSchema = z.object({
  title: z
    .string()
    .trim()
    .min(1, { error: 'editorial.course.form.titleRequired' satisfies MessageKey })
    .max(courseTitleMaxLength, { error: 'editorial.course.form.titleTooLong' satisfies MessageKey })
    .refine((title) => !title.includes('\u0000'), { error: 'editorial.course.form.titleHasNul' satisfies MessageKey }),
});

const { defineField, errors, handleSubmit } = useForm({
  validationSchema: toTypedSchema(courseSchema),
  initialValues: { title: '' },
});
const [title, titleAttributes] = defineField('title');
const { t } = useI18n();

// A refused title takes the focus, so the editor corrects it where the refusal is read.
const submit = handleSubmit(
  async (values) => {
    await onCreate(values.title);
  },
  () => document.getElementById(titleField)?.focus(),
);
</script>

<template>
  <form novalidate :aria-label="t('editorial.course.form.label')" class="flex flex-col gap-4" @submit="submit">
    <div class="flex flex-col gap-2">
      <label :for="titleField" class="text-body font-medium">{{ t('editorial.course.form.title') }}</label>
      <Input
        :id="titleField"
        v-model="title"
        v-bind="titleAttributes"
        autocomplete="off"
        aria-required="true"
        :aria-invalid="errors.title ? true : undefined"
        :aria-describedby="errors.title ? `${titleField}-error` : undefined"
      />
      <p v-if="errors.title" :id="`${titleField}-error`" role="alert" class="text-caption text-destructive">
        {{ t(errors.title) }}
      </p>
    </div>
    <div class="flex justify-end gap-2">
      <slot name="actions" />
      <Button type="submit" :disabled="saving">{{ t('editorial.course.form.submit') }}</Button>
    </div>
  </form>
</template>
