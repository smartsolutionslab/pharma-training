<script setup lang="ts">
import { useForm } from 'vee-validate';
import { z } from 'zod';
import { OrganisationJobFunction } from '@/api/generated';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { toTypedSchema } from '@/forms/toTypedSchema';
import { useRegisterPharmacy } from './useRegisterPharmacy';

// Registers a pharmacy, with the user as its manager: its name and what the user does there, checked here as the host
// checks them (PharmacyName, OrganisationJobFunction) before the host checks them again.
const nameField = 'pharmacy-name';
const jobFunctionField = 'pharmacy-job-function';

const registrationSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, 'Give the pharmacy a name.')
    .max(120, "A pharmacy's name is at most 120 characters.")
    .refine((name) => !name.includes('\u0000'), "A pharmacy's name cannot contain a nul character."),
  jobFunction: z.enum(OrganisationJobFunction, { error: 'Choose what you do in the pharmacy.' }),
});

const jobFunctions: { value: OrganisationJobFunction; label: string }[] = [
  { value: OrganisationJobFunction.Pharmacist, label: 'Pharmacist' },
  { value: OrganisationJobFunction.Pta, label: 'PTA (pharmaceutical-technical assistant)' },
  { value: OrganisationJobFunction.Pka, label: 'PKA (pharmaceutical-commercial employee)' },
  { value: OrganisationJobFunction.Trainee, label: 'Trainee' },
  { value: OrganisationJobFunction.Other, label: 'Other' },
];

const { defineField, errors, handleSubmit } = useForm({
  validationSchema: toTypedSchema(registrationSchema),
  initialValues: { name: '', jobFunction: '' },
});
const [name, nameAttributes] = defineField('name');
const [jobFunction, jobFunctionAttributes] = defineField('jobFunction');

const { register, isPending } = useRegisterPharmacy();
const submit = handleSubmit((values) => register(values));
</script>

<template>
  <main class="flex max-w-xl flex-col gap-4 p-4">
    <div class="flex flex-col gap-1">
      <h1 class="text-title">Register a pharmacy</h1>
      <p class="text-body text-muted-foreground">
        You will manage the pharmacy: invite your team and assign their training.
      </p>
    </div>
    <form novalidate aria-label="Register a pharmacy" class="flex flex-col gap-4" @submit="submit">
      <div class="flex flex-col gap-2">
        <label :for="nameField" class="text-body font-medium">Pharmacy name</label>
        <Input
          :id="nameField"
          v-model="name"
          v-bind="nameAttributes"
          autocomplete="organization"
          :aria-invalid="errors.name ? true : undefined"
          :aria-describedby="errors.name ? `${nameField}-error` : undefined"
        />
        <p v-if="errors.name" :id="`${nameField}-error`" role="alert" class="text-caption text-destructive">
          {{ errors.name }}
        </p>
      </div>
      <div class="flex flex-col gap-2">
        <label :for="jobFunctionField" class="text-body font-medium">Your job function</label>
        <select
          :id="jobFunctionField"
          v-model="jobFunction"
          v-bind="jobFunctionAttributes"
          :aria-invalid="errors.jobFunction ? true : undefined"
          :aria-describedby="errors.jobFunction ? `${jobFunctionField}-error` : undefined"
          class="h-9 w-full min-w-0 rounded-md border border-input bg-transparent px-3 py-1 text-base shadow-xs motion-safe:transition-[color,box-shadow] outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-destructive/20 md:text-sm dark:bg-input/30 dark:aria-invalid:ring-destructive/40 [&_option]:bg-popover [&_option]:text-popover-foreground"
        >
          <option value="" disabled>Choose…</option>
          <option v-for="option in jobFunctions" :key="option.value" :value="option.value">{{ option.label }}</option>
        </select>
        <p
          v-if="errors.jobFunction"
          :id="`${jobFunctionField}-error`"
          role="alert"
          class="text-caption text-destructive"
        >
          {{ errors.jobFunction }}
        </p>
      </div>
      <Button type="submit" class="self-start" :disabled="isPending">Register pharmacy</Button>
    </form>
  </main>
</template>
