<script setup lang="ts">
import { Award } from '@lucide/vue';
import { computed, h, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import type { CertificationCertificateDto } from '@/api/generated';
import { Button } from '@/components/ui/button';
import DataTable from '@/components/ui/DataTable.vue';
import { columnsOf, translatedHeader } from '@/components/ui/data-table-columns';
import { EmptyState } from '@/components/ui/empty-state';
import { i18n, t as translate } from '@/i18n';
import { calendarDate } from '@/i18n/formats';
import { notify } from '@/lib/notify';
import { certificatesFailure, downloadFailure } from './certificateErrors';
import CertificateValidity from './CertificateValidity.vue';
import { parseCertificatesPage } from './certificatesQuery';
import { downloadCertificate } from './downloadCertificate';
import { titleKey } from './titleKey';
import { useMyCertificates } from './useMyCertificates';

// The signed-in user's certificates (#296, Z1), newest first, a page at a time: the course, the days it was issued and
// is valid until, whether it is valid, and whether its PDF is ready. A ready one's PDF is downloaded from its row and
// saved under the course's name. The page is the address's (?page=n), read through parseCertificatesPage, so an invalid
// value never reaches the host.
const day = (value: string) => i18n.global.d(calendarDate(value), 'date');

const certificates = columnsOf<CertificationCertificateDto>();
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
const none: CertificationCertificateDto[] = [];

const route = useRoute();
const router = useRouter();
const shownPage = computed(() => parseCertificatesPage(route.query));
const { certificates: rows, page, isPending, error } = useMyCertificates(shownPage);
const { t } = useI18n();

const failure = computed(() => (error.value ? certificatesFailure(error.value) : undefined));
/** The certificate whose PDF is on its way, if any: its button waits, the others stay. */
const downloading = ref<string>();

/** Shows another page of the certificates. */
function turnTo(next: number) {
  void router.push({ query: { page: String(next) } });
}

/** Saves the certificate's PDF; a failure is reported, naming the course. */
async function download(certificate: CertificationCertificateDto) {
  if (downloading.value !== undefined) {
    return;
  }
  downloading.value = certificate.certificateId;
  try {
    await downloadCertificate(certificate);
  } catch (refusal) {
    const failed = downloadFailure(refusal);
    notify.failure('key' in failed ? translate(failed.key, { course: certificate.courseTitle }) : failed.message);
  } finally {
    downloading.value = undefined;
  }
}
</script>

<template>
  <main class="flex flex-col gap-6">
    <h1 class="text-title">{{ t(titleKey) }}</h1>
    <p v-if="failure" role="alert">{{ 'key' in failure ? t(failure.key) : failure.message }}</p>
    <DataTable v-else :columns="columns" :data="rows ?? none" :loading="isPending" :paging="page" @update:page="turnTo">
      <template #actions="{ row }">
        <Button
          v-if="row.documentReady"
          type="button"
          variant="outline"
          size="sm"
          :aria-label="t('certification.mine.downloadFor', { course: row.courseTitle })"
          :disabled="downloading === row.certificateId"
          :aria-busy="downloading === row.certificateId || undefined"
          @click="download(row)"
        >
          {{ t('certification.mine.download') }}
        </Button>
      </template>
      <template #empty>
        <EmptyState>
          <template #icon><Award /></template>
          {{ t('certification.mine.empty') }}
          <template #action>
            <Button as-child variant="outline">
              <RouterLink to="/learning">{{ t('certification.mine.toCourses') }}</RouterLink>
            </Button>
          </template>
        </EmptyState>
      </template>
    </DataTable>
  </main>
</template>
