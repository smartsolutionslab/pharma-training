<script setup lang="ts">
import { useForm } from 'vee-validate';
import { useI18n } from 'vue-i18n';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import { toTypedSchema } from '@/forms/toTypedSchema';
import { t as translate } from '@/i18n';

interface CreateEditorialSampleFormProps {
  /** Creates the sample; resolves to whether it was created, which empties the field. */
  onCreate: (name: string) => Promise<boolean>;
  saving: boolean;
}

// Creates one of the module's samples: a name, checked here before the host checks it again.
const { onCreate, saving } = defineProps<CreateEditorialSampleFormProps>();

/** The id of the form's name field, which the page's empty state moves to through focus. */
const sampleNameField = 'editorial-sample-name';

const sampleSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, { error: () => translate('samples.form.nameRequired') })
    .max(100, { error: () => translate('samples.form.nameTooLong') }),
});

const { defineField, errors, handleSubmit, resetForm } = useForm({
  validationSchema: toTypedSchema(sampleSchema),
  initialValues: { name: '' },
});
const [name, nameAttributes] = defineField('name');
const { t } = useI18n();

const submit = handleSubmit(async (values) => {
  if (await onCreate(values.name)) {
    resetForm();
  }
});

defineExpose({ focus: () => document.getElementById(sampleNameField)?.focus() });
</script>

<template>
  <form novalidate :aria-label="t('samples.form.label')" class="flex flex-col gap-2" @submit="submit">
    <label :for="sampleNameField" class="text-sm font-medium">{{ t('samples.form.name') }}</label>
    <div class="flex gap-2">
      <input
        :id="sampleNameField"
        v-model="name"
        v-bind="nameAttributes"
        autocomplete="off"
        :aria-invalid="errors.name ? true : undefined"
        :aria-describedby="errors.name ? `${sampleNameField}-error` : undefined"
        class="flex h-9 w-full min-w-0 rounded-md border border-input bg-transparent px-3 py-1 text-base shadow-xs motion-safe:transition-[color,box-shadow] outline-none placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-destructive/20 md:text-sm dark:bg-input/30 dark:aria-invalid:ring-destructive/40"
      />
      <Button type="submit" :disabled="saving">{{ t('samples.form.submit') }}</Button>
    </div>
    <p v-if="errors.name" :id="`${sampleNameField}-error`" role="alert" class="text-sm text-destructive">
      {{ errors.name }}
    </p>
  </form>
</template>
