<script setup lang="ts">
import { Award } from '@lucide/vue';
import { computed, h, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import type { HeldCertificateDto } from '@/api/generated';
import { Button } from '@/components/ui/button';
import DataTable from '@/components/ui/DataTable.vue';
import { columnsOf, translatedHeader } from '@/components/ui/data-table-columns';
import { EmptyState } from '@/components/ui/empty-state';
import { i18n, t as translate } from '@/i18n';
import { calendarDate } from '@/i18n/formats';
import { messageText } from '@/i18n/errorText';
import { notify } from '@/lib/notify';
import { route as learning } from '@/modules/learning/routes';
import { useCanReach } from '@/shell/useCanReach';
import { certificatesFailure, documentFailure } from './certificateErrors';
import CertificateValidity from './CertificateValidity.vue';
import { parseCertificatesPage } from './certificatesQuery';
import { openCertificate } from './openCertificate';
import { titleKey } from './titleKey';
import { useMyCertificates } from './useMyCertificates';

// The signed-in user's certificates (#296, Z1), newest first, a page at a time: the course, the days it was issued and
// is valid until, whether it is valid, and whether its PDF is ready. A ready one's PDF opens from its row, in this tab.
// Without certificates, the empty state leads to My courses only a user who may see that page. The page is the address's (?page=n), read through parseCertificatesPage, so an invalid
// value never reaches the host.
const day = (value: string) => i18n.global.d(calendarDate(value), 'date');

const certificates = columnsOf<HeldCertificateDto>();
const columns = certificates.columns([
  certificates.accessor('courseTitle', translatedHeader('certification.mine.columns.course')),
  certificates.accessor('issuedOn', {
    ...translatedHeader('certification.mine.columns.issuedOn'),
    cell: ({ row }) => day(row.original.issuedOn),
  }),
  certificates.accessor('validUntil', {
    ...translatedHeader('certification.mine.columns.validUntil'),
    cell: ({ row }) => day(row.original.validUntil),
  }),
  certificates.accessor('validity', {
    ...translatedHeader('certification.mine.columns.validity'),
    cell: ({ row }) => h(CertificateValidity, { validity: row.original.validity }),
  }),
  certificates.accessor('documentReady', {
    ...translatedHeader('certification.mine.columns.document'),
    cell: ({ row }) =>
      translate(row.original.documentReady ? 'certification.mine.documentReady' : 'certification.mine.documentPending'),
  }),
]);
const none: HeldCertificateDto[] = [];

const route = useRoute();
const router = useRouter();
const shownPage = computed(() => parseCertificatesPage(route.query));
const { certificates: rows, page, isPending, error } = useMyCertificates(shownPage);
const { t } = useI18n();

const failure = computed(() => (error.value ? certificatesFailure(error.value) : undefined));
/** The certificate whose PDF is being asked for, if any: its button waits, the others stay. */
const opening = ref<string>();
/** My courses, for a user who may see it, by the shell's own rule and its own route's audience. */
const showsCourses = useCanReach(learning.audience);

/** Shows another page of the certificates. */
function turnTo(next: number) {
  void router.push({ query: { page: String(next) } });
}

/** Opens the certificate's PDF in this tab; a failure is reported, naming the course, and the page stays. */
async function open(certificate: HeldCertificateDto) {
  if (opening.value !== undefined) {
    return;
  }
  opening.value = certificate.certificateId;
  try {
    await openCertificate(certificate.certificateId);
  } catch (refusal) {
    notify.failure(messageText(documentFailure(refusal, certificate.courseTitle)));
  } finally {
    opening.value = undefined;
  }
}
</script>

<template>
  <main class="flex flex-col gap-6">
    <h1 class="text-title">{{ t(titleKey) }}</h1>
    <p v-if="failure" role="alert">{{ messageText(failure) }}</p>
    <DataTable v-else :columns="columns" :data="rows ?? none" :loading="isPending" :paging="page" @update:page="turnTo">
      <template #actions="{ row }">
        <Button
          v-if="row.documentReady"
          type="button"
          variant="outline"
          size="sm"
          :aria-label="t('certification.mine.openFor', { course: row.courseTitle })"
          :disabled="opening === row.certificateId"
          :aria-busy="opening === row.certificateId || undefined"
          @click="open(row)"
        >
          {{ t('certification.mine.open') }}
        </Button>
      </template>
      <template #empty>
        <EmptyState>
          <template #icon><Award /></template>
          {{ t('certification.mine.empty') }}
          <template v-if="showsCourses" #action>
            <Button as-child variant="outline">
              <RouterLink :to="`/${learning.path}`">{{ t('certification.mine.toCourses') }}</RouterLink>
            </Button>
          </template>
        </EmptyState>
      </template>
    </DataTable>
  </main>
</template>
