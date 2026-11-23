<script setup lang="ts">
import { BookOpenCheck } from '@lucide/vue';
import { useQueryClient } from '@tanstack/vue-query';
import { computed, ref, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';
import { ApiError } from '@/api/baseApi';
import { getGetOrganisationPharmaciesMineQueryKey } from '@/api/generated';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-state';
import { useCurrentPharmacy } from '@/shell/useCurrentPharmacy';
import MyCoursesList from './MyCoursesList.vue';
import { myCoursesFailure } from './myCoursesErrors';
import { parseMyCoursesQuery, toLocationQuery } from './myCoursesQuery';
import { titleKey } from './titleKey';
import { useMyCourses } from './useMyCourses';

// My courses (#38): the caller's assigned courses in the current pharmacy, in the host's order, each with Start or
// Continue. Only the page is in the address. Whether the caller may see them the host decides (ADR 0003), and a refusal
// takes the place of the list.

const { t } = useI18n();
const route = useRoute();
const router = useRouter();
const queryClient = useQueryClient();
const { current } = useCurrentPharmacy();
const list = useTemplateRef<InstanceType<typeof MyCoursesList>>('list');

const address = computed(() => parseMyCoursesQuery(route.query));

// Another pharmacy chosen lists its courses from the first page: the address loses its page, and until it has, the
// first page is asked, never the old pharmacy's page of the new one. Synchronous, so the query never sees the other.
const switching = ref(false);
watch(
  () => current.value?.id,
  (id, previous) => {
    if (id === undefined || previous === undefined || id === previous) {
      return;
    }
    switching.value = true;
    void router.replace({ query: toLocationQuery({ page: 1 }) }).finally(() => {
      switching.value = false;
    });
  },
  { flush: 'sync' },
);
const page = computed(() => (switching.value ? 1 : address.value.page));

const { assignments, items, paging } = useMyCourses(current, page);
const { data, error, isPending, isFetching } = assignments;

const failure = computed(() => (error.value === null ? undefined : myCoursesFailure(error.value)));
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
  return shown !== undefined && 'key' in shown && shown.key === 'learning.myCourses.loadFailed';
});
/** The list shows unless a refusal takes its place; a background refetch that failed keeps the courses it had. */
const showsList = computed(() => failure.value === undefined || data.value !== undefined);

// The host counts the caller a member no more: the memberships are read again, so the shell lets go of this one.
watch(error, (refusal) => {
  if (refusal instanceof ApiError && refusal.code === 'compliance.pharmacy-not-found') {
    void queryClient.invalidateQueries({ queryKey: getGetOrganisationPharmaciesMineQueryKey() });
  }
});

// Once settled, a page past the last one is replaced by the last, or by the first with nothing to show. Replaced, not
// pushed: Back leaves the page instead of returning to an address that shows nothing.
watch([data, isFetching], ([shown, fetching]) => {
  if (shown === undefined || fetching || switching.value || shown.items.length > 0) {
    return;
  }
  const last = Math.max(1, Math.ceil(Number(shown.totalCount) / Number(shown.pageSize)));
  if (address.value.page > last) {
    void router.replace({ query: toLocationQuery({ page: last }) });
  }
});

function choosePage(next: number) {
  void router.push({ query: toLocationQuery({ page: next }) });
}

// After Retry the focus stays on its button until the alert goes; once the courses are shown, it moves to the list's
// heading, since the button that held it is gone. (Read again without data, the query is pending until it answers.)
const retried = ref(false);

function retry() {
  retried.value = true;
  void assignments.refetch();
}

watch(
  [failure, isPending],
  ([shown, pending]) => {
    if (shown === undefined && !pending && retried.value) {
      retried.value = false;
      list.value?.focusHeading();
    }
  },
  { flush: 'post' },
);
</script>

<template>
  <main class="flex flex-col gap-6">
    <div class="flex flex-col gap-2">
      <h1 class="text-title">{{ t(titleKey) }}</h1>
      <p v-if="current !== undefined" class="text-body text-muted-foreground">
        {{ t('learning.myCourses.intro', { pharmacy: current.name }) }}
      </p>
    </div>
    <p v-if="failureText !== undefined" role="alert" class="text-body text-destructive">
      {{ failureText }}
      <Button v-if="canRetry" type="button" variant="outline" class="ml-2" @click="retry">
        {{ t('learning.myCourses.retry') }}
      </Button>
    </p>
    <MyCoursesList
      v-if="showsList"
      ref="list"
      :items="items"
      :paging="paging"
      :pending="isPending"
      :busy="isFetching"
      @update:page="choosePage"
    >
      <template #empty>
        <EmptyState>
          <template #icon><BookOpenCheck /></template>
          {{ t('learning.myCourses.empty', { pharmacy: current?.name ?? '' }) }}
          <template v-if="current?.canAssignCourses" #action>
            <Button as-child>
              <RouterLink to="/compliance/assign">{{ t('modules.compliance.pages.assign.title') }}</RouterLink>
            </Button>
          </template>
        </EmptyState>
      </template>
    </MyCoursesList>
  </main>
</template>
