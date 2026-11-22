<script setup lang="ts">
import { BookOpen } from '@lucide/vue';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRoute } from 'vue-router';
import { useGetEditorialCourseVersionsId } from '@/api/generated';
import { ApiError } from '@/api/baseApi';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-state';
import { Skeleton } from '@/components/ui/skeleton';
import ChapterList from './ChapterList.vue';
import { inboxStatuses } from './inboxQuery';

// A course version (/editorial/versions/:id): its title, number and status, the way back to its inbox tab, and its
// chapters. Only a draft can be changed; any other status says why not and shows its chapters read-only.
const route = useRoute();
const id = computed(() => String(route.params.id ?? ''));
const { data: version, isPending, isError, error } = useGetEditorialCourseVersionsId(id);
const { t } = useI18n();
/** Whether the chapter form is open to add a chapter. */
const adding = ref(false);

const notFound = computed(() => error.value instanceof ApiError && error.value.status === 404);
const editable = computed(() => version.value?.status === 'Draft');
/** The inbox tab the version is listed in; a Superseded or Retired one is in none, so the inbox's default. */
const inbox = computed(() => {
  const status = version.value?.status;
  return inboxStatuses.some((each) => each === status) ? `/editorial?status=${status}` : '/editorial';
});
</script>

<template>
  <main class="flex flex-col gap-6">
    <template v-if="isPending">
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
      </header>
      <p v-if="version.status !== 'Draft'" class="text-body bg-muted rounded-md p-3">
        {{ t(`editorial.version.readOnly.${version.status}`) }}
      </p>
      <section aria-labelledby="course-version-chapters" class="flex flex-col gap-4">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <h2 id="course-version-chapters">{{ t('editorial.chapters.title') }}</h2>
          <Button v-if="editable && version.chapters.length > 0" type="button" @click="adding = true">
            {{ t('editorial.chapters.add') }}
          </Button>
        </div>
        <ChapterList v-if="version.chapters.length > 0" :chapters="version.chapters" :editable="editable" />
        <EmptyState v-else>
          <template #icon><BookOpen /></template>
          {{ t('editorial.chapters.empty') }}
          <template v-if="editable" #action>
            <Button type="button" variant="outline" @click="adding = true">{{ t('editorial.chapters.add') }}</Button>
          </template>
        </EmptyState>
      </section>
    </template>
  </main>
</template>
