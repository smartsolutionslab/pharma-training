<script setup lang="ts">
import { Inbox } from '@lucide/vue';
import { computed, h, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import type { EditorialCourseVersionSummaryDto } from '@/api/generated';
import { Button } from '@/components/ui/button';
import DataTable from '@/components/ui/DataTable.vue';
import { columnsOf, translatedHeader } from '@/components/ui/data-table-columns';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { EmptyState } from '@/components/ui/empty-state';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { i18n, t as translate } from '@/i18n';
import { notify } from '@/lib/notify';
import CreateCourseForm from './CreateCourseForm.vue';
import { inboxStatuses, parseInboxQuery, type InboxQuery } from './inboxQuery';
import { titleKey } from './titleKey';
import { useCourseVersions } from './useCourseVersions';
import { useCreateCourse } from './useCreateCourse';

// The editors' inbox: the course versions of a status, Draft, InReview or Published, a page at a time, each a link to
// its page. Its state is the address's (?status&page), read through parseInboxQuery, so an invalid value never reaches
// the host. "Neue Schulung" starts a course in a dialog and opens its first version.
const versions = columnsOf<EditorialCourseVersionSummaryDto>();
const columns = versions.columns([
  versions.accessor('title', {
    ...translatedHeader('editorial.inbox.columns.title'),
    cell: ({ row }) =>
      h(
        RouterLink,
        { to: `/editorial/versions/${row.original.id}`, class: 'text-primary underline-offset-4 hover:underline' },
        () => row.original.title,
      ),
  }),
  versions.accessor('number', {
    ...translatedHeader('editorial.inbox.columns.number'),
    cell: ({ row }) => i18n.global.n(Number(row.original.number), 'integer'),
  }),
  versions.accessor('status', {
    ...translatedHeader('editorial.inbox.columns.status'),
    cell: ({ row }) => translate(`glossary.courseVersionStatus.${row.original.status}`),
  }),
]);
const none: EditorialCourseVersionSummaryDto[] = [];

const route = useRoute();
const router = useRouter();
const query = computed(() => parseInboxQuery(route.query));
const { versions: rows, page, isPending, isError } = useCourseVersions(query);
const creating = ref(false);

// A page past the last one (an old link, a list that got shorter) shows the last page, which takes its place in the
// address: Back then leaves the inbox instead of returning to a page that does not exist.
watch(page, (shown) => {
  if (shown === undefined || isPending.value) {
    return;
  }
  const last = Math.ceil(Number(shown.totalCount) / Number(shown.pageSize));
  if (shown.items.length === 0 && last > 0 && Number(shown.page) > last) {
    void router.replace({ query: { status: query.value.status, page: String(last) } });
  }
});
const { create, isSaving } = useCreateCourse();
const { t } = useI18n();

/** Starts a course and opens its first version; a refusal is reported, and the dialog stays open with the title. */
async function onCreate(title: string) {
  const result = await create(title);
  if (!result.ok) {
    notify.failure(result.serverMessage ?? translate('editorial.course.createFailed', { title }));
    return false;
  }
  notify.success(translate('editorial.course.created', { title }));
  creating.value = false;
  await router.push(`/editorial/versions/${result.id}`);
  return true;
}

/** Shows a status from its first page. */
function choose(status: string | number) {
  if (inboxStatuses.some((each) => each === status)) {
    void router.push({ query: { status: String(status) } });
  }
}

/** Shows another page of the status shown. */
function turnTo(next: InboxQuery['page']) {
  void router.push({ query: { status: query.value.status, page: String(next) } });
}
</script>

<template>
  <main class="flex flex-col gap-6">
    <h1 class="text-title">{{ t(titleKey) }}</h1>
    <section aria-labelledby="editorial-inbox" class="flex flex-col gap-4">
      <div class="flex flex-wrap items-center justify-between gap-2">
        <h2 id="editorial-inbox">{{ t('editorial.inbox.title') }}</h2>
        <Button type="button" @click="creating = true">{{ t('editorial.course.new') }}</Button>
      </div>
      <Tabs :model-value="query.status" @update:model-value="choose">
        <TabsList :aria-label="t('editorial.inbox.statuses')">
          <TabsTrigger v-for="status in inboxStatuses" :key="status" :value="status">
            {{ t(`glossary.courseVersionStatus.${status}`) }}
          </TabsTrigger>
        </TabsList>
        <TabsContent v-for="status in inboxStatuses" :key="status" :value="status">
          <p v-if="isError" role="alert">{{ t('editorial.inbox.loadFailed') }}</p>
          <DataTable
            v-else
            :columns="columns"
            :data="rows ?? none"
            :loading="isPending"
            :paging="page"
            @update:page="turnTo"
          >
            <template #empty>
              <EmptyState>
                <template #icon><Inbox /></template>
                {{ t(`editorial.inbox.empty.${status}`) }}
                <template v-if="status === 'Draft'" #action>
                  <Button type="button" variant="outline" @click="creating = true">
                    {{ t('editorial.course.new') }}
                  </Button>
                </template>
              </EmptyState>
            </template>
          </DataTable>
        </TabsContent>
      </Tabs>
    </section>
    <Dialog v-model:open="creating">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ t('editorial.course.form.label') }}</DialogTitle>
          <DialogDescription>{{ t('editorial.course.form.description') }}</DialogDescription>
        </DialogHeader>
        <CreateCourseForm :on-create="onCreate" :saving="isSaving">
          <template #actions>
            <DialogClose as-child>
              <Button type="button" variant="outline">{{ t('editorial.cancel') }}</Button>
            </DialogClose>
          </template>
        </CreateCourseForm>
      </DialogContent>
    </Dialog>
  </main>
</template>
