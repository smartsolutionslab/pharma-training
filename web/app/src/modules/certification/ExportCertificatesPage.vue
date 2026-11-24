<script setup lang="ts">
import { useForm } from 'vee-validate';
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useQueryClient } from '@tanstack/vue-query';
import {
  getCertificationPharmaciesIdCertificatesExport,
  getGetOrganisationPharmaciesMineQueryKey,
} from '@/api/generated';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { toTypedSchema } from '@/forms/toTypedSchema';
import { i18n, type MessageKey, t as translate } from '@/i18n';
import { type ErrorMessage, messageText } from '@/i18n/errorText';
import { notify } from '@/lib/notify';
import { useCurrentPharmacy } from '@/shell/useCurrentPharmacy';
import { exportRefusal } from './exportErrors';
import { defaultPeriod, type ExportPeriod, exportSchema } from './exportSchema';

// The managers' certificate export (#297, ADR 0032): the issue days from and to, the last twelve months in the German
// day to begin with, and the ZIP the host streams for the current pharmacy, saved under the host's file name. The ZIP
// is fetched with the plain call, never a query: nothing else reads it, and the cache would hold its megabytes. Hiding
// the page from anyone but a manager is the shell's convenience; the host decides who may export (ADR 0003).
const clock = () => new Date();

/** How long a saved ZIP's object URL lives: Safari fails a download whose URL is revoked in the task of the click. */
const objectUrlLifetime = 30_000;

const fromField = 'export-from';
const toField = 'export-to';

const { t } = useI18n();
const { current } = useCurrentPharmacy();
const queryClient = useQueryClient();

const { defineField, errors, handleSubmit, isSubmitting, setFieldError } = useForm({
  validationSchema: toTypedSchema(exportSchema()),
  initialValues: defaultPeriod(clock),
});
const [from, fromAttributes] = defineField('from');
const [to, toAttributes] = defineField('to');

/** Why the last export failed, under the form; the form stays, so submitting again retries. */
const failure = ref<ErrorMessage>();
/** The request is on its way: from the moment it is sent, after the range was checked, to its answer. */
const sending = ref(false);
/** A press now sends nothing; the button stays enabled, so it keeps the keyboard's focus. */
const busy = computed(() => isSubmitting.value || sending.value);
/** The error at to is the host's period refusal, not the schema's. */
const hostRefusedTo = ref(false);

// Another pharmacy's refusal says nothing about this one; the range stays.
watch(
  () => current.value?.id,
  () => {
    failure.value = undefined;
    if (hostRefusedTo.value) {
      hostRefusedTo.value = false;
      setFieldError('to', undefined);
    }
  },
);

/** The object URLs of saved ZIPs not let go yet, with the timer that lets each go. */
const pendingUrls = new Map<string, ReturnType<typeof setTimeout>>();

function release(url: string) {
  const timer = pendingUrls.get(url);
  if (timer === undefined) {
    return;
  }
  clearTimeout(timer);
  pendingUrls.delete(url);
  URL.revokeObjectURL(url);
}

/** Cancels the export on its way when the page goes; an answer that still arrives saves nothing. */
const leaving = new AbortController();

onBeforeUnmount(() => {
  leaving.abort();
  for (const url of [...pendingUrls.keys()]) {
    release(url);
  }
});

/** A field's error: a key of the catalog, as the schema gives it. */
const fieldText = (error: string) => (i18n.global.te(error) ? translate(error as MessageKey) : error);

/** Saves the bytes under the name through a temporary link; the object URL goes 30 seconds later or with the page. */
function save(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  document.body.append(link);
  link.click();
  link.remove();
  pendingUrls.set(
    url,
    setTimeout(() => release(url), objectUrlLifetime),
  );
}

/** Takes the focus to the field, once its error shows, where the error is read out. */
async function focusField(id: string) {
  await nextTick();
  document.getElementById(id)?.focus();
}

/** Shows the refusal by its code: the period's at to, any other in the alert under the form. */
function refuse(error: unknown) {
  const refusal = exportRefusal(error);
  if (refusal.kind === 'period') {
    setFieldError('to', messageText(refusal.message));
    hostRefusedTo.value = true;
    void focusField(toField);
    return;
  }
  failure.value = refusal.message;
  if (refusal.pharmacyGone) {
    // The membership is gone: the switcher and the audience catch up with the memberships read again.
    void queryClient.invalidateQueries({ queryKey: getGetOrganisationPharmaciesMineQueryKey() });
  }
}

/** Asks for the ZIP and saves it; whether it was saved. */
async function send(period: ExportPeriod): Promise<boolean> {
  const pharmacy = current.value;
  if (pharmacy === undefined) {
    return false;
  }
  failure.value = undefined;
  hostRefusedTo.value = false;
  sending.value = true;
  try {
    const answer = await getCertificationPharmaciesIdCertificatesExport(pharmacy.id, period, {
      signal: leaving.signal,
    });
    if (leaving.signal.aborted) {
      return false;
    }
    // The host's own name; its pattern where the answer names none.
    const name = answer instanceof File ? answer.name : `teilnahmebestaetigungen_${period.from}_${period.to}.zip`;
    save(answer, name);
    return true;
  } catch (error) {
    // Cancelled because the page went: nobody is there to be told.
    if (leaving.signal.aborted) {
      return false;
    }
    refuse(error);
    return false;
  } finally {
    sending.value = false;
  }
}

const submitForm = handleSubmit(send, ({ errors: refused }) => {
  // The first field in error, in the form's order.
  if (refused.from !== undefined) {
    void focusField(fromField);
  } else if (refused.to !== undefined) {
    void focusField(toField);
  }
});

/** Sends once, however often the button is pressed: a press while one is on its way does nothing. */
async function submit(event: Event) {
  if (busy.value) {
    event.preventDefault();
    return;
  }
  // Said once the form is done with it, the button ready for the next export.
  if ((await submitForm(event)) === true) {
    notify.success(translate('certification.export.done'));
  }
}
</script>

<template>
  <main class="flex max-w-2xl flex-col gap-6">
    <h1 class="text-title">{{ t('modules.certification.pages.export.title') }}</h1>
    <p v-if="current !== undefined" class="text-body">
      {{ t('certification.export.intro', { pharmacy: current.name }) }}
    </p>
    <form class="flex flex-col gap-4" novalidate @submit="submit">
      <div class="flex flex-wrap gap-4">
        <div class="flex flex-col gap-2">
          <label :for="fromField" class="text-body font-medium">{{ t('certification.export.from') }}</label>
          <Input
            :id="fromField"
            v-model="from"
            v-bind="fromAttributes"
            type="date"
            aria-required="true"
            :aria-invalid="errors.from ? true : undefined"
            :aria-describedby="errors.from ? `${fromField}-error` : undefined"
            class="w-fit"
          />
          <p v-if="errors.from" :id="`${fromField}-error`" role="alert" class="text-caption text-destructive">
            {{ fieldText(errors.from) }}
          </p>
        </div>
        <div class="flex flex-col gap-2">
          <label :for="toField" class="text-body font-medium">{{ t('certification.export.to') }}</label>
          <Input
            :id="toField"
            v-model="to"
            v-bind="toAttributes"
            type="date"
            aria-required="true"
            :aria-invalid="errors.to ? true : undefined"
            :aria-describedby="errors.to ? `${toField}-error` : undefined"
            class="w-fit"
          />
          <p v-if="errors.to" :id="`${toField}-error`" role="alert" class="text-caption text-destructive">
            {{ fieldText(errors.to) }}
          </p>
        </div>
      </div>
      <div>
        <Button
          type="submit"
          :aria-disabled="busy ? 'true' : undefined"
          :aria-busy="busy ? 'true' : undefined"
          class="aria-disabled:cursor-not-allowed aria-disabled:opacity-50"
        >
          {{ sending ? t('certification.export.preparing') : t('certification.export.submit') }}
        </Button>
      </div>
      <p role="status" class="sr-only">
        <template v-if="sending">{{ t('certification.export.preparing') }}</template>
      </p>
      <p v-if="failure !== undefined" role="alert" class="text-body text-destructive">{{ messageText(failure) }}</p>
    </form>
  </main>
</template>
