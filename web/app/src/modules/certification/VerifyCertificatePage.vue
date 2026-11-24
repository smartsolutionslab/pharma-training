<script setup lang="ts">
import { CircleAlert } from '@lucide/vue';
import { computed, nextTick, useTemplateRef } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute } from 'vue-router';
import { useGetCertificationVerifyCode } from '@/api/generated';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { calendarDate } from '@/i18n/formats';
import CertificateValidity from './CertificateValidity.vue';
import { parseVerificationCode } from './verificationCode';
import { verificationOutcome } from './verificationErrors';

// The verification page (#54, Z2) a certificate's QR code opens, for anyone, signed in or not: whether the certificate
// is valid or expired first, then its holder, course and dates, and nothing else. A malformed code is never sent. Every
// request spends the limit every verifier shares (ADR 0026): no automatic retry, no refetch on focus, and one request
// per press of Retry.
const { t, d } = useI18n();
const route = useRoute();
const heading = useTemplateRef<HTMLHeadingElement>('heading');

const code = computed(() => parseVerificationCode(route.params));

const { data, error, isPending, refetch } = useGetCertificationVerifyCode(() => code.value ?? '', {
  query: {
    enabled: computed(() => code.value !== undefined),
    retry: false,
    refetchOnWindowFocus: false,
    staleTime: Infinity,
  },
});

const outcome = computed(() => (error.value === null ? undefined : verificationOutcome(error.value)));
const notVerifiable = computed(() => code.value === undefined || outcome.value?.kind === 'notVerifiable');
const alert = computed(() => {
  const shown = outcome.value;
  if (shown === undefined || shown.kind === 'notVerifiable') {
    return undefined;
  }
  return 'key' in shown ? t(shown.key) : shown.message;
});

const day = (value: string) => d(calendarDate(value), 'date');

async function retry() {
  await refetch();
  if (error.value === null) {
    await nextTick();
    heading.value?.focus();
  }
}
</script>

<template>
  <main class="mx-auto flex w-full max-w-md min-w-0 flex-col gap-6 px-4 py-6">
    <h1 ref="heading" tabindex="-1" class="text-title focus:outline-none">
      {{ t('modules.certification.entries.verify.title') }}
    </h1>

    <section
      v-if="notVerifiable"
      aria-labelledby="notVerifiableHeading"
      class="flex flex-col gap-2 rounded-md border bg-background p-4"
    >
      <h2 id="notVerifiableHeading" class="flex items-center gap-2 text-body font-semibold">
        <CircleAlert aria-hidden="true" class="size-5 shrink-0 text-muted-foreground" />
        <span>{{ t('certification.verify.notVerifiable.title') }}</span>
      </h2>
      <p class="text-body">{{ t('certification.verify.notVerifiable.text') }}</p>
    </section>

    <div v-else-if="alert !== undefined" class="flex flex-col items-start gap-3">
      <p role="alert" class="text-body">{{ alert }}</p>
      <Button type="button" variant="outline" @click="retry">{{ t('certification.retry') }}</Button>
    </div>

    <template v-else-if="data !== undefined">
      <section aria-labelledby="resultHeading" class="flex flex-col gap-4 rounded-md border bg-background p-4">
        <div class="flex flex-col gap-1">
          <h2 id="resultHeading" class="text-body font-semibold">
            <CertificateValidity :validity="data.validity" />
          </h2>
          <p class="text-body">
            {{
              t(data.validity === 'Valid' ? 'certification.verify.validText' : 'certification.verify.expiredText', {
                date: day(data.validUntil),
              })
            }}
          </p>
        </div>
        <dl class="grid min-w-0 grid-cols-1 gap-x-4 gap-y-1 sm:grid-cols-[auto_1fr]">
          <dt class="text-caption text-muted-foreground">{{ t('certification.verify.holder') }}</dt>
          <dd class="min-w-0 text-body break-words">{{ data.holderName }}</dd>
          <dt class="text-caption text-muted-foreground">{{ t('certification.verify.course') }}</dt>
          <dd class="min-w-0 text-body break-words">{{ data.courseTitle }}</dd>
          <dt class="text-caption text-muted-foreground">{{ t('certification.verify.issuedOn') }}</dt>
          <dd class="text-body tabular-nums">{{ day(data.issuedOn) }}</dd>
          <dt class="text-caption text-muted-foreground">{{ t('certification.verify.validUntil') }}</dt>
          <dd class="text-body tabular-nums">{{ day(data.validUntil) }}</dd>
        </dl>
      </section>
    </template>

    <template v-else-if="isPending">
      <p role="status" class="sr-only">{{ t('dataTable.loading') }}</p>
      <div aria-hidden="true" class="flex flex-col gap-4 rounded-md border bg-background p-4">
        <Skeleton class="h-6 w-32" />
        <Skeleton class="h-4 w-full" />
        <Skeleton class="h-4 w-3/4" />
        <Skeleton class="h-4 w-1/2" />
      </div>
    </template>
  </main>
</template>
