<script setup lang="ts">
import { useForm } from 'vee-validate';
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { toTypedSchema } from '@/forms/toTypedSchema';
import type { MessageKey } from '@/i18n';
import { blueprintBounds, type TestBlueprint, testBlueprintSchema } from './testBlueprintSchema';

interface TestBlueprintFormProps {
  /** The blueprint the form starts from, the version's. */
  blueprint: TestBlueprint;
  /** Saves the blueprint; resolves to whether it was saved. */
  onSave: (blueprint: TestBlueprint) => Promise<boolean>;
  saving: boolean;
}

const { blueprint, onSave, saving } = defineProps<TestBlueprintFormProps>();
const emit = defineEmits<{ cancel: [] }>();

const { defineField, errors, handleSubmit } = useForm({
  validationSchema: toTypedSchema(testBlueprintSchema),
  initialValues: { ...blueprint },
});
const [questions, questionsAttributes] = defineField('questions');
const [passingScorePercent, passingScoreAttributes] = defineField('passingScorePercent');
const [validityMonths, validityAttributes] = defineField('validityMonths');
const { t, n } = useI18n();

/** The fields in the order they are shown, which their messages follow. */
const fields = [
  { name: 'questions', id: 'blueprint-questions', label: 'editorial.test.blueprint.questions' },
  { name: 'passingScorePercent', id: 'blueprint-passing-score', label: 'editorial.test.blueprint.passingScore' },
  { name: 'validityMonths', id: 'blueprint-validity', label: 'editorial.test.blueprint.validity' },
] as const satisfies { name: keyof TestBlueprint; id: string; label: MessageKey }[];
const models = { questions, passingScorePercent, validityMonths };
const attributes = {
  questions: questionsAttributes,
  passingScorePercent: passingScoreAttributes,
  validityMonths: validityAttributes,
};

/** Whether a submit was refused: the messages show from then on, all of them at once. */
const refused = ref(false);

/** A field's message, once a submit was refused: its bounds formatted in the language of the moment. */
function message(name: keyof TestBlueprint): string | undefined {
  const key = refused.value ? errors.value[name] : undefined;
  if (key === undefined) {
    return undefined;
  }
  const { min, max } = blueprintBounds[name];
  return t(key, { min: n(min, 'integer'), max: n(max, 'integer') });
}

// A refused field takes the focus, the first one, so the editor corrects it where the refusal is read.
const submit = handleSubmit(
  async (values) => {
    await onSave(values);
  },
  ({ errors: found }) => {
    refused.value = true;
    const first = fields.find((field) => found[field.name] !== undefined);
    if (first !== undefined) {
      document.getElementById(first.id)?.focus();
    }
  },
);
</script>

<template>
  <form novalidate class="flex flex-col gap-4" @submit="submit">
    <div v-for="field in fields" :key="field.name" class="flex flex-col gap-2">
      <label :for="field.id" class="text-body font-medium">{{ t(field.label) }}</label>
      <Input
        :id="field.id"
        v-model="models[field.name].value"
        v-bind="attributes[field.name].value"
        type="number"
        inputmode="numeric"
        step="1"
        :min="blueprintBounds[field.name].min"
        :max="blueprintBounds[field.name].max"
        aria-required="true"
        class="max-w-40 tabular-nums"
        :aria-invalid="message(field.name) ? true : undefined"
        :aria-describedby="message(field.name) ? `${field.id}-error` : undefined"
      />
      <p v-if="message(field.name)" :id="`${field.id}-error`" role="alert" class="text-caption text-destructive">
        {{ message(field.name) }}
      </p>
    </div>
    <div class="flex justify-end gap-2">
      <Button type="button" variant="outline" @click="emit('cancel')">{{ t('editorial.cancel') }}</Button>
      <Button type="submit" :disabled="saving">{{ t('editorial.test.blueprint.submit') }}</Button>
    </div>
  </form>
</template>
