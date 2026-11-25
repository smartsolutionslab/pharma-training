<script setup lang="ts">
import { useForm } from 'vee-validate';
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQueryClient } from '@tanstack/vue-query';
import { getGetOrganisationPharmaciesMineQueryKey, type MyPharmacyDto } from '@/api/generated';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { toTypedSchema } from '@/forms/toTypedSchema';
import { i18n, type MessageKey, t as translate } from '@/i18n';
import { type ErrorMessage, messageText } from '@/i18n/errorText';
import { notify } from '@/lib/notify';
import { retentionRefusal } from './retentionErrors';
import { type RetentionPeriod, retentionSchema, retentionYears } from './retentionSchema';
import { useRetentionPeriod } from './useRetentionPeriod';

// The pharmacy's retention period on the export page (#419; ADR 0034, amended by ADR 0039): how many years its
// certificates stay in the export, and a field to change it. It shows only once the host has answered the read: a
// refusal or a failure hides it without a word, since the host decides who may read and set it (ADR 0003), and the
// export above reports a membership that is gone itself.

interface RetentionPeriodSectionProps {
  /** The current pharmacy, whose period the section shows and sets. */
  pharmacy: MyPharmacyDto;
}

const { pharmacy } = defineProps<RetentionPeriodSectionProps>();

const field = 'retention-years';
const titleId = 'retention-title';
const currentId = 'retention-current';
const noteId = 'retention-note';
const errorId = `${field}-error`;

const { t, n } = useI18n();
const queryClient = useQueryClient();
const { period, save, isSaving } = useRetentionPeriod(() => pharmacy.id);

/** The period as the host last answered it for this pharmacy; undefined while it is not known or was refused. */
const shown = computed(() => {
  const data = period.data.value;
  return period.isSuccess.value && data !== undefined
    ? { years: Number(data.years), isDefault: data.isDefault }
    : undefined;
});

const { defineField, errors, handleSubmit, isSubmitting, resetForm, submitCount } = useForm({
  validationSchema: toTypedSchema(retentionSchema),
  initialValues: { years: shown.value?.years ?? Number.NaN },
});
const [years, yearsAttributes] = defineField('years');

/** The host's refusal of the period last sent, shown at the field until the period changes or is sent again. */
const hostRefusal = ref<ErrorMessage>();
watch(years, () => {
  hostRefusal.value = undefined;
});

/**
 * Which form a press was made on: the count goes up when the form starts again from the host's period and when the
 * section goes. A press whose check ends after that sends nothing, so 31 pressed for one pharmacy never sends the
 * next one's period in its place.
 */
let generation = 0;
/** Whether the section has gone: a press dropped then says nothing, there is nobody left to tell. */
let gone = false;
onBeforeUnmount(() => {
  generation += 1;
  gone = true;
});
// Another pharmacy starts a new form at once, not only when its period arrives: a press for the one before, whose check
// ends while the new period is on its way, would otherwise send the old pharmacy's years to the new one.
watch(
  () => pharmacy.id,
  () => {
    generation += 1;
  },
);

// The field starts from the period the host answered, and again whenever that changes: another pharmacy, or the read
// after a save. An error at the field goes with it.
watch(
  () => (shown.value === undefined ? undefined : `${pharmacy.id}:${shown.value.years}:${shown.value.isDefault}`),
  (key) => {
    if (key !== undefined && shown.value !== undefined) {
      generation += 1;
      resetForm({ values: { years: shown.value.years } });
      hostRefusal.value = undefined;
    }
  },
  { immediate: true },
);

/** The field's error once a save was asked for: the catalog's text with the bounds, as the host's 400 has it. */
const fieldError = computed(() => {
  if (hostRefusal.value !== undefined) {
    return messageText(hostRefusal.value);
  }
  const error = submitCount.value > 0 ? errors.value.years : undefined;
  if (error === undefined) {
    return undefined;
  }
  return i18n.global.te(error) ? messageText({ key: error as MessageKey, values: { ...retentionYears } }) : error;
});

/** The period now, and whether it is the default nobody chose. */
const status = computed(() => {
  if (shown.value === undefined) {
    return '';
  }
  const current = t('certification.retention.current', { years: n(shown.value.years, 'integer') });
  return shown.value.isDefault ? `${current} ${t('certification.retention.default')}` : current;
});

/** The request is on its way, from the moment it is sent, after the field was checked, to its answer. */
const sending = ref(false);
/** A press now sends nothing; the button stays enabled, so it keeps the keyboard's focus. */
const busy = computed(() => isSubmitting.value || sending.value || isSaving.value);

/** Takes the focus to the field, once its error shows, where the error is read out. */
async function focusField() {
  await nextTick();
  document.getElementById(field)?.focus();
}

/**
 * Shows the refusal by its code: the period's at the field, any other in a toast. The period is read again after any
 * save (useRetentionPeriod): refused now, the read is likely refused too, and the section goes; the toast says why.
 */
function refuse(error: unknown) {
  const refusal = retentionRefusal(error);
  if (refusal.kind === 'field') {
    hostRefusal.value = refusal.message;
    void focusField();
    return;
  }
  notify.failure(messageText(refusal.message));
  if (refusal.pharmacyGone) {
    // The membership is gone: the switcher and the audience catch up with the memberships read again.
    void queryClient.invalidateQueries({ queryKey: getGetOrganisationPharmaciesMineQueryKey() });
  }
}

/** Sends the years to the pharmacy the press was made for, never to the one current when the check ends. */
async function send({ years: chosen }: RetentionPeriod, pharmacyId: string) {
  hostRefusal.value = undefined;
  sending.value = true;
  try {
    const change = await save(pharmacyId, chosen);
    if (change.ok) {
      notify.success(translate('certification.retention.saved', { years: n(chosen, 'integer') }));
    } else {
      refuse(change.error);
    }
  } finally {
    sending.value = false;
  }
}

/**
 * How many presses have been checked, whether the check passed or not, and so were sent or dropped. It stands on the
 * section's root, which stays while another pharmacy's period is read, so a test can tell when a press's check has
 * ended without waiting a while.
 */
const checked = ref(0);

/** Sends once, however often the button is pressed: a press while one is on its way does nothing. */
async function submit(event: Event) {
  if (busy.value) {
    event.preventDefault();
    return;
  }
  const pressed = generation;
  const pressedFor = pharmacy.id;
  await handleSubmit(
    async (values) => {
      checked.value += 1;
      if (pressed === generation && pressedFor === pharmacy.id) {
        await send(values, pressedFor);
      } else if (!gone && pressedFor === pharmacy.id) {
        // The period changed while the press was checked, and the field started again from it: the years typed were
        // not sent, which is said, since the field now shows another value. Another pharmacy chosen says nothing.
        notify.failure(translate('certification.retention.changed'));
      }
    },
    () => {
      checked.value += 1;
      if (pressed === generation) {
        void focusField();
      }
    },
  )(event);
}
</script>

<template>
  <div class="contents" :data-presses-checked="checked">
    <section v-if="shown !== undefined" :aria-labelledby="titleId" class="flex flex-col gap-4">
      <h2 :id="titleId" class="text-body font-semibold">{{ t('certification.retention.title') }}</h2>
      <p class="text-body">{{ t('certification.retention.intro', { pharmacy: pharmacy.name }) }}</p>
      <p :id="currentId" class="text-body font-medium tabular-nums">{{ status }}</p>
      <p :id="noteId" class="text-caption text-muted-foreground">{{ t('certification.retention.retroactive') }}</p>
      <form class="flex flex-col gap-4" novalidate @submit="submit">
        <div class="flex flex-col gap-2">
          <label :for="field" class="text-body font-medium">{{ t('certification.retention.years') }}</label>
          <Input
            :id="field"
            v-model="years"
            v-bind="yearsAttributes"
            type="number"
            inputmode="numeric"
            step="1"
            :min="retentionYears.min"
            :max="retentionYears.max"
            aria-required="true"
            :aria-invalid="fieldError ? true : undefined"
            :aria-describedby="fieldError ? `${currentId} ${noteId} ${errorId}` : `${currentId} ${noteId}`"
            class="max-w-40 tabular-nums"
          />
          <p v-if="fieldError" :id="errorId" role="alert" class="text-caption text-destructive">{{ fieldError }}</p>
        </div>
        <div>
          <Button
            type="submit"
            :aria-disabled="busy ? 'true' : undefined"
            :aria-busy="busy ? 'true' : undefined"
            class="aria-disabled:cursor-not-allowed aria-disabled:opacity-50"
          >
            {{ sending ? t('certification.retention.saving') : t('certification.retention.submit') }}
          </Button>
        </div>
        <p role="status" class="sr-only">
          <template v-if="sending">{{ t('certification.retention.saving') }}</template>
        </p>
      </form>
    </section>
  </div>
</template>
