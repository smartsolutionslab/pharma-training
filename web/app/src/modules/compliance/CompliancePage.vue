<script setup lang="ts">
import { keepPreviousData, useQueryClient } from '@tanstack/vue-query';
import { computed, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute } from 'vue-router';
import { ApiError } from '@/api/baseApi';
import { getGetOrganisationPharmaciesMineQueryKey, useGetCompliancePharmaciesIdMatrix } from '@/api/generated';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useCurrentPharmacy } from '@/shell/useCurrentPharmacy';
import { matrixFailure } from './matrixErrors';
import { parseMatrixQuery } from './matrixQuery';
import { titleKey } from './titleKey';

// The team matrix (#36): the current pharmacy's members by the courses assigned there, the manager's main page. The
// filters and the page are in the address; whether the member may see the matrix the host decides (ADR 0003), and a
// refusal takes the place of the matrix.

/** A pharmacy has tens of members: one page of 50 shows them all, or nearly. */
const pageSize = 50;

const { t } = useI18n();
const route = useRoute();
const queryClient = useQueryClient();
const { current } = useCurrentPharmacy();

const query = computed(() => parseMatrixQuery(route.query));

const matrix = useGetCompliancePharmaciesIdMatrix(
  () => current.value?.id ?? '',
  () => ({ page: query.value.page, pageSize, jobFunction: query.value.jobFunction }),
  { query: { enabled: computed(() => current.value !== undefined), placeholderData: keepPreviousData } },
);
const { data, error, isPending, isFetching } = matrix;

const failure = computed(() => (error.value === null ? undefined : matrixFailure(error.value)));
const failureText = computed(() => {
  const shown = failure.value;
  if (shown === undefined) {
    return undefined;
  }
  return 'key' in shown ? t(shown.key) : shown.message;
});
/** Only a failure, not a refusal, is worth reading again. */
const canRetry = computed(() => {
  const shown = failure.value;
  return shown !== undefined && 'key' in shown && shown.key === 'compliance.matrix.loadFailed';
});
/** The matrix shows unless a refusal takes its place; a background refetch that failed keeps the data it had. */
const showsMatrix = computed(() => failure.value === undefined || data.value !== undefined);

// The host counts the caller a member no more: the memberships are read again, so the shell lets go of this one.
watch(error, (refusal) => {
  if (refusal instanceof ApiError && refusal.code === 'compliance.pharmacy-not-found') {
    void queryClient.invalidateQueries({ queryKey: getGetOrganisationPharmaciesMineQueryKey() });
  }
});

function retry() {
  void matrix.refetch();
}
</script>

<template>
  <main class="flex flex-col gap-6">
    <div class="flex flex-wrap items-start justify-between gap-4">
      <div class="flex flex-col gap-2">
        <h1 class="text-title">{{ t(titleKey) }}</h1>
        <p v-if="current !== undefined" class="text-body text-muted-foreground">
          {{ t('compliance.matrix.intro', { pharmacy: current.name }) }}
        </p>
      </div>
      <Button v-if="current?.canAssignCourses" as-child>
        <RouterLink to="/compliance/assign">{{ t('modules.compliance.pages.assign.title') }}</RouterLink>
      </Button>
    </div>
    <p v-if="failureText !== undefined" role="alert" class="text-body text-destructive">
      {{ failureText }}
      <Button v-if="canRetry" type="button" variant="outline" class="ml-2" @click="retry">
        {{ t('compliance.matrix.retry') }}
      </Button>
    </p>
    <div
      v-if="showsMatrix"
      role="region"
      :aria-label="t('compliance.matrix.table.label')"
      tabindex="0"
      :aria-busy="isFetching || undefined"
      class="max-h-[70vh] overflow-auto rounded-md border"
    >
      <div v-if="isPending" aria-hidden="true" class="flex flex-col gap-2 p-4">
        <div v-for="row in 3" :key="row" class="flex gap-4">
          <Skeleton v-for="column in 4" :key="column" class="h-8 w-36" />
        </div>
      </div>
    </div>
  </main>
</template>
