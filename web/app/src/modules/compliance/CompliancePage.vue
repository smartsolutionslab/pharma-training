<script setup lang="ts">
import { ClipboardList, Users } from '@lucide/vue';
import { useQueryClient } from '@tanstack/vue-query';
import { computed, ref, useId, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';
import { ApiError } from '@/api/baseApi';
import { type ComplianceJobFunction, getGetOrganisationPharmaciesMineQueryKey } from '@/api/generated';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-state';
import { messageText } from '@/i18n/errorText';
import { useCurrentPharmacy } from '@/shell/useCurrentPharmacy';
import ComplianceMatrixTable from './ComplianceMatrixTable.vue';
import MatrixFilters from './MatrixFilters.vue';
import MatrixLegend from './MatrixLegend.vue';
import MatrixRate from './MatrixRate.vue';
import MemberComplianceSheet from './MemberComplianceSheet.vue';
import { matrixFailure, matrixRetryable } from './matrixErrors';
import { type MatrixQuery, parseMatrixQuery, toLocationQuery } from './matrixQuery';
import { titleKey } from './titleKey';
import { useTeamMatrix } from './useTeamMatrix';

// The team matrix (#36): the current pharmacy's members by the courses assigned there, the manager's main page. The
// filters and the page are in the address; whether the member may see the matrix the host decides (ADR 0003), and a
// refusal takes the place of the matrix.

/** Today, read when the page renders: a completion past its last valid day shows as expired. */
const clock = () => new Date();

const { t } = useI18n();
const route = useRoute();
const router = useRouter();
const queryClient = useQueryClient();
const { current } = useCurrentPharmacy();
const teamHeadingId = useId();
const filters = useTemplateRef<InstanceType<typeof MatrixFilters>>('filters');

const query = computed(() => parseMatrixQuery(route.query));

const { matrix, courses, course, columns, rows, paging, tally } = useTeamMatrix(current, query);
const { data, error, isPending, isFetching } = matrix;

const failure = computed(() => (error.value === null ? undefined : matrixFailure(error.value)));
const failureText = computed(() => (failure.value === undefined ? undefined : messageText(failure.value)));
/** Any failure is worth reading again but a refusal a second read cannot turn round (matrixRetryable). */
const canRetry = computed(() => error.value !== null && matrixRetryable(error.value));
/** The matrix shows unless a refusal takes its place; a background refetch that failed keeps the data it had. */
const showsMatrix = computed(() => failure.value === undefined || data.value !== undefined);

/** What the team section shows once the matrix is loaded: the table, or why there is nothing to show. */
const content = computed<'table' | 'noCourses' | 'noMembers'>(() => {
  if (isPending.value || data.value === undefined) {
    return 'table';
  }
  if (courses.value.length === 0) {
    return 'noCourses';
  }
  return rows.value.length === 0 ? 'noMembers' : 'table';
});

// The host counts the caller a member no more: the memberships are read again, so the shell lets go of this one.
watch(error, (refusal) => {
  if (refusal instanceof ApiError && refusal.code === 'compliance.pharmacy-not-found') {
    void queryClient.invalidateQueries({ queryKey: getGetOrganisationPharmaciesMineQueryKey() });
  }
});

// Once the matrix has settled, the address loses what it cannot show: a course the matrix does not have (an old link,
// another pharmacy's), and a page past the last one, which the last page replaces. Replaced, not pushed: Back leaves the
// matrix instead of returning to an address that shows nothing.
watch([data, isFetching], ([shown, fetching]) => {
  if (shown === undefined || fetching) {
    return;
  }
  const settled: MatrixQuery = { ...query.value };
  if (settled.course !== undefined && course.value === undefined) {
    delete settled.course;
  }
  const last = Math.ceil(Number(shown.members.totalCount) / Number(shown.members.pageSize));
  if (shown.members.items.length === 0 && last > 0 && Number(shown.members.page) > last) {
    settled.page = last;
  }
  if (settled.course !== query.value.course || settled.page !== query.value.page) {
    void router.replace({ query: toLocationQuery(settled) });
  }
});

function show(next: MatrixQuery) {
  return router.push({ query: toLocationQuery(next) });
}

function chooseCourse(chosen: string | undefined) {
  void show({ ...query.value, course: chosen });
}

function chooseJobFunction(chosen: ComplianceJobFunction | undefined) {
  void show({ ...query.value, jobFunction: chosen, page: 1 });
}

function choosePage(page: number) {
  void show({ ...query.value, page });
}

async function reset() {
  await router.push({ path: '/compliance' });
  filters.value?.focusJobFunction();
}

function retry() {
  void matrix.refetch();
}

/** The member whose sheet is open, and the name that opened it, where the focus returns. */
const chosen = ref<{ personId: string; opener: HTMLElement }>();
const chosenRow = computed(() => rows.value.find((row) => row.personId === chosen.value?.personId));

function openPerson(personId: string, opener: HTMLElement) {
  chosen.value = { personId, opener };
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
      <div class="flex flex-wrap gap-2">
        <!-- The records an inspection asks for (#297): the export's audience is the matrix's, its handler decides. -->
        <Button variant="outline" as-child>
          <RouterLink to="/certification/export">{{ t('modules.certification.pages.export.title') }}</RouterLink>
        </Button>
        <Button v-if="current?.canAssignCourses" as-child>
          <RouterLink to="/compliance/assign">{{ t('modules.compliance.pages.assign.title') }}</RouterLink>
        </Button>
      </div>
    </div>
    <p v-if="failureText !== undefined" role="alert" class="text-body text-destructive">
      {{ failureText }}
      <Button v-if="canRetry" type="button" variant="outline" class="ml-2" @click="retry">
        {{ t('compliance.matrix.retry') }}
      </Button>
    </p>
    <template v-if="showsMatrix">
      <MatrixRate :tally="tally" :course="course?.title" :job-function="query.jobFunction" />
      <section :aria-labelledby="teamHeadingId" class="flex min-w-0 flex-col gap-4">
        <h2 :id="teamHeadingId" class="text-body font-semibold">{{ t('compliance.matrix.table.title') }}</h2>
        <MatrixFilters
          v-if="data !== undefined"
          ref="filters"
          :courses="courses"
          :course="course?.courseId"
          :job-function="query.jobFunction"
          @update:course="chooseCourse"
          @update:job-function="chooseJobFunction"
          @reset="reset"
        />
        <MatrixLegend />
        <EmptyState v-if="content === 'noCourses'">
          <template #icon><ClipboardList /></template>
          {{ t('compliance.matrix.empty.noCourses') }}
          <template v-if="current?.canAssignCourses" #action>
            <Button as-child>
              <RouterLink to="/compliance/assign">{{ t('modules.compliance.pages.assign.title') }}</RouterLink>
            </Button>
          </template>
        </EmptyState>
        <EmptyState v-else-if="content === 'noMembers'">
          <template #icon><Users /></template>
          {{
            t('compliance.matrix.empty.noMembers', {
              jobFunction:
                query.jobFunction === undefined
                  ? t('compliance.matrix.filters.allJobFunctions')
                  : t(`glossary.jobFunction.${query.jobFunction}`),
            })
          }}
          <template v-if="query.jobFunction !== undefined" #action>
            <Button type="button" variant="outline" @click="reset">{{ t('compliance.matrix.filters.reset') }}</Button>
          </template>
        </EmptyState>
        <ComplianceMatrixTable
          v-else
          :columns="columns"
          :rows="rows"
          :paging="paging"
          :pending="isPending"
          :busy="isFetching"
          :today="clock()"
          @update:page="choosePage"
          @open="openPerson"
        />
      </section>
      <MemberComplianceSheet
        :row="chosenRow"
        :courses="courses"
        :today="clock()"
        :opener="chosen?.opener"
        @close="chosen = undefined"
      />
    </template>
  </main>
</template>
