<script setup lang="ts">
import { useForm } from 'vee-validate';
import { useI18n } from 'vue-i18n';
import { z } from 'zod';
import { OrganisationJobFunction } from '@/api/generated';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { toTypedSchema } from '@/forms/toTypedSchema';
import type { MessageKey } from '@/i18n';
import { useRegisterPharmacy } from './useRegisterPharmacy';

// Registers a pharmacy, with the user as its manager: its name and what the user does there, checked here as the host
// checks them (PharmacyName, OrganisationJobFunction) before the host checks them again.
const nameField = 'pharmacy-name';
const jobFunctionField = 'pharmacy-job-function';

// Each message is a key, rendered with t where it is shown: a message already shown follows a switch of the language.
const registrationSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, { error: 'organisation.register.nameRequired' satisfies MessageKey })
    .max(120, { error: 'organisation.register.nameTooLong' satisfies MessageKey })
    .refine((name) => !name.includes('\u0000'), { error: 'organisation.register.nameHasNul' satisfies MessageKey }),
  jobFunction: z.enum(OrganisationJobFunction, {
    error: 'organisation.register.jobFunctionRequired' satisfies MessageKey,
  }),
});

// Their labels are the glossary's, looked up by the API's value when the page renders, in the language of the moment.
const jobFunctions = [
  OrganisationJobFunction.Pharmacist,
  OrganisationJobFunction.Pta,
  OrganisationJobFunction.Pka,
  OrganisationJobFunction.Trainee,
  OrganisationJobFunction.Other,
] as const;

const { t } = useI18n();

const { defineField, errors, handleSubmit, isSubmitting } = useForm({
  validationSchema: toTypedSchema(registrationSchema),
  initialValues: { name: '', jobFunction: '' },
});
const [name, nameAttributes] = defineField('name');
const [jobFunction, jobFunctionAttributes] = defineField('jobFunction');

// The form is submitting until register has settled, the memberships read again and the page moved on included: a
// submit meanwhile is ignored, or a second click would register a second pharmacy. The button is disabled from the
// moment the registration is on its way until the page has moved on.
const { register, isBusy } = useRegisterPharmacy();
const registerOnce = handleSubmit((values) => register(values));
function submit(event: Event) {
  if (isSubmitting.value) {
    event.preventDefault();
    return;
  }
  void registerOnce(event);
}
</script>

<template>
  <main class="flex max-w-xl flex-col gap-4 p-4">
    <div class="flex flex-col gap-1">
      <h1 class="text-title">{{ t('organisation.register.title') }}</h1>
      <p class="text-body text-muted-foreground">{{ t('organisation.register.intro') }}</p>
    </div>
    <form novalidate :aria-label="t('organisation.register.title')" class="flex flex-col gap-4" @submit="submit">
      <div class="flex flex-col gap-2">
        <label :for="nameField" class="text-body font-medium">{{ t('organisation.register.name') }}</label>
        <Input
          :id="nameField"
          v-model="name"
          v-bind="nameAttributes"
          autocomplete="organization"
          aria-required="true"
          :aria-invalid="errors.name ? true : undefined"
          :aria-describedby="errors.name ? `${nameField}-error` : undefined"
        />
        <p v-if="errors.name" :id="`${nameField}-error`" role="alert" class="text-caption text-destructive">
          {{ t(errors.name) }}
        </p>
      </div>
      <div class="flex flex-col gap-2">
        <label :for="jobFunctionField" class="text-body font-medium">{{
          t('organisation.register.jobFunction')
        }}</label>
        <select
          :id="jobFunctionField"
          v-model="jobFunction"
          v-bind="jobFunctionAttributes"
          aria-required="true"
          :aria-invalid="errors.jobFunction ? true : undefined"
          :aria-describedby="errors.jobFunction ? `${jobFunctionField}-error` : undefined"
          class="h-9 w-full min-w-0 rounded-md border border-input bg-transparent px-3 py-1 text-body motion-safe:transition-[color,box-shadow] outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-destructive/20 dark:bg-input/30 dark:aria-invalid:ring-destructive/40 [&_option]:bg-popover [&_option]:text-popover-foreground"
        >
          <option value="" disabled>{{ t('organisation.register.choose') }}</option>
          <option v-for="option in jobFunctions" :key="option" :value="option">
            {{ t(`glossary.jobFunction.${option}`) }}
          </option>
        </select>
        <p
          v-if="errors.jobFunction"
          :id="`${jobFunctionField}-error`"
          role="alert"
          class="text-caption text-destructive"
        >
          {{ t(errors.jobFunction) }}
        </p>
      </div>
      <Button type="submit" class="self-start" :disabled="isBusy">{{ t('organisation.register.submit') }}</Button>
    </form>
  </main>
</template>
