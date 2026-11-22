<script setup lang="ts">
import { BookOpen } from '@lucide/vue';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRoute } from 'vue-router';
import { z } from 'zod';
import {
  type EditorialChapterDto,
  type EditorialChapterRequest,
  useGetEditorialCourseVersionsId,
} from '@/api/generated';
import { ApiError } from '@/api/baseApi';
import { Button } from '@/components/ui/button';
import { Dialog, DialogClose, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { EmptyState } from '@/components/ui/empty-state';
import { Skeleton } from '@/components/ui/skeleton';
import { t as translate } from '@/i18n';
import { notify } from '@/lib/notify';
import ChapterForm from './ChapterForm.vue';
import ChapterList from './ChapterList.vue';
import { inboxStatuses } from './inboxQuery';
import RemoveChapterDialog from './RemoveChapterDialog.vue';
import { useChapterMutations } from './useChapterMutations';
import { useReorderChapters } from './useReorderChapters';

// A course version (/editorial/versions/:id): its title, number and status, the way back to its inbox tab, and its
// chapters. Only a draft can be changed; any other status says why not and shows its chapters read-only.
const route = useRoute();
const id = computed(() => String(route.params.id ?? ''));
/** The id goes into the requests' paths: one that is no UUID names no version, and is never sent. */
const validId = computed(() => z.uuid().safeParse(id.value).success);
const {
  data: version,
  isPending,
  isError,
  error,
} = useGetEditorialCourseVersionsId(id, { query: { enabled: validId } });
const { add, replace, remove, isSaving } = useChapterMutations(id);
const { reorder } = useReorderChapters(id);
const { t } = useI18n();

/** The chapter form while it is open: with the chapter it replaces, or none to add one. */
const form = ref<{ chapter?: EditorialChapterDto }>();
/** The chapter whose deletion is being asked about. */
const removing = ref<EditorialChapterDto>();
const isRemoving = ref(false);
/** Whether the chapter form's file is on its way to storage: the dialog stays open until it is there or cancelled. */
const chapterUploading = ref(false);

const notFound = computed(() => error.value instanceof ApiError && error.value.status === 404);
const editable = computed(() => version.value?.status === 'Draft');
/** The inbox tab the version is listed in; a Superseded or Retired one is in none, so the inbox's default. */
const inbox = computed(() => {
  const status = version.value?.status;
  return inboxStatuses.some((each) => each === status) ? `/editorial?status=${status}` : '/editorial';
});

/** Saves the form's chapter; a refusal is reported, and the dialog stays open with what was entered. */
async function save(request: EditorialChapterRequest) {
  const chapter = form.value?.chapter;
  const result = chapter === undefined ? await add(request) : await replace(chapter.id, request);
  if (!result.ok) {
    notify.failure(result.serverMessage ?? translate('editorial.chapters.saveFailed', { title: request.title }));
    return false;
  }
  notify.success(translate('editorial.chapters.saved', { title: request.title }));
  form.value = undefined;
  return true;
}

/** Deletes the chapter asked about, and reports how that went. */
async function confirmRemove(chapter: EditorialChapterDto) {
  isRemoving.value = true;
  const result = await remove(chapter.id);
  isRemoving.value = false;
  removing.value = undefined;
  if (result.ok) {
    notify.success(translate('editorial.chapters.removed', { title: chapter.title }));
  } else {
    notify.failure(result.serverMessage ?? translate('editorial.chapters.removeFailed', { title: chapter.title }));
  }
}
</script>

<template>
  <main class="flex flex-col gap-6">
    <p v-if="!validId" class="text-body">{{ t('editorial.version.notFound') }}</p>
    <template v-else-if="isPending">
      <div class="flex flex-col gap-2">
        <Skeleton class="h-8 w-2/3 max-w-md" />
        <Skeleton class="h-4 w-40" />
      </div>
      <div class="flex flex-col gap-2">
        <Skeleton v-for="row in 3" :key="row" class="h-14 w-full" />
      </div>
    </template>
    <p v-else-if="notFound" class="text-body">{{ t('editorial.version.notFound') }}</p>
    <p v-else-if="isError || version === undefined" role="alert" class="text-body text-destructive">
      {{ t('editorial.version.loadFailed') }}
    </p>
    <template v-else>
      <header class="flex flex-col gap-2">
        <RouterLink :to="inbox" class="text-caption text-primary underline-offset-4 hover:underline">
          {{ t('editorial.version.backToInbox') }}
        </RouterLink>
        <h1 class="text-title">{{ version.title }}</h1>
        <div class="text-caption text-muted-foreground flex flex-wrap items-center gap-3">
          <span class="tabular-nums">{{ t('editorial.version.number', { number: version.number }) }}</span>
          <span class="bg-secondary text-secondary-foreground rounded-md px-2 py-0.5">{{
            t(`glossary.courseVersionStatus.${version.status}`)
          }}</span>
        </div>
        <RouterLink
          :to="`/editorial/versions/${id}/test`"
          class="text-body text-primary self-start underline-offset-4 hover:underline"
        >
          {{ t('editorial.version.openTest') }}
        </RouterLink>
      </header>
      <p v-if="version.status !== 'Draft'" class="text-body bg-muted rounded-md p-3">
        {{ t(`editorial.version.readOnly.${version.status}`) }}
      </p>
      <section aria-labelledby="course-version-chapters" class="flex flex-col gap-4">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <h2 id="course-version-chapters">{{ t('editorial.chapters.title') }}</h2>
          <Button v-if="editable && version.chapters.length > 0" type="button" @click="form = {}">
            {{ t('editorial.chapters.add') }}
          </Button>
        </div>
        <ChapterList
          v-if="version.chapters.length > 0"
          :chapters="version.chapters"
          :editable="editable"
          @edit="(chapter) => (form = { chapter })"
          @remove="(chapter) => (removing = chapter)"
          @reorder="reorder"
        />
        <EmptyState v-else>
          <template #icon><BookOpen /></template>
          {{ t('editorial.chapters.empty') }}
          <template v-if="editable" #action>
            <Button type="button" variant="outline" @click="form = {}">{{ t('editorial.chapters.add') }}</Button>
          </template>
        </EmptyState>
      </section>
      <template v-if="editable">
        <Dialog :open="form !== undefined" @update:open="(open) => !open && !chapterUploading && (form = undefined)">
          <DialogContent class="sm:max-w-2xl" :aria-describedby="undefined" :close-disabled="chapterUploading">
            <DialogHeader>
              <DialogTitle>
                {{ t(form?.chapter ? 'editorial.chapterForm.editTitle' : 'editorial.chapterForm.addTitle') }}
              </DialogTitle>
            </DialogHeader>
            <ChapterForm
              v-if="form !== undefined"
              :version-id="id"
              :chapter="form.chapter"
              :questions="version.questions"
              :on-save="save"
              :saving="isSaving"
              @busy="(busy) => (chapterUploading = busy)"
            >
              <template #actions="{ busy }">
                <DialogClose as-child>
                  <Button type="button" variant="outline" :disabled="busy">{{ t('editorial.cancel') }}</Button>
                </DialogClose>
              </template>
            </ChapterForm>
          </DialogContent>
        </Dialog>
        <RemoveChapterDialog
          :chapter="removing"
          :questions="version.questions"
          :removing="isRemoving"
          @confirm="confirmRemove"
          @cancel="removing = undefined"
        />
      </template>
    </template>
  </main>
</template>
