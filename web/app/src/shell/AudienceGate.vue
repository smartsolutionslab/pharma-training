<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { useHasRole } from '@/auth/useHasRole';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { canReach, pageOf } from './audience';
import { useCurrentPharmacy } from './useCurrentPharmacy';

// The page at the address, if it is for the user; otherwise why not, and the way home. Hiding is a convenience, not
// protection: the backend's policies decide. While the memberships load a pharmacy's page is a skeleton, so neither
// the page nor "Not available" flashes. A page below a module is governed by its own audience, not its module's.
const route = useRoute();
const { status, memberships, current, retry } = useCurrentPharmacy();
const isEditor = useHasRole('editor');

const page = computed(() => pageOf(route.path));

const state = computed<'page' | 'loading' | 'refused'>(() => {
  if (page.value === undefined) {
    return 'page';
  }
  if (canReach(page.value.audience, { current: current.value, isEditor: isEditor.value })) {
    return 'page';
  }
  return page.value.audience !== 'editor' && status.value === 'loading' ? 'loading' : 'refused';
});

const failed = computed(() => page.value?.audience !== 'editor' && status.value === 'failed');

const reason = computed(() => {
  if (page.value?.audience === 'editor') {
    return 'This page is not available to you.';
  }
  if (failed.value) {
    return 'Your pharmacies could not be loaded.';
  }
  if (current.value !== undefined) {
    return `This page is not available to you in ${current.value.name}.`;
  }
  return memberships.value.length === 0
    ? 'You are not a member of any pharmacy yet.'
    : 'Choose a pharmacy to see this page.';
});
</script>

<template>
  <slot v-if="state === 'page'" />
  <main v-else-if="state === 'loading'" class="flex flex-col gap-3 p-4" aria-busy="true">
    <span class="sr-only">Loading…</span>
    <Skeleton class="h-8 w-1/3" />
    <Skeleton class="h-4 w-2/3" />
    <Skeleton class="h-4 w-1/2" />
  </main>
  <main v-else class="flex flex-col items-start gap-2 p-4">
    <h1 class="text-title">Not available</h1>
    <p class="text-body">{{ reason }}</p>
    <Button v-if="failed" variant="outline" @click="retry">Try again</Button>
    <RouterLink to="/" class="text-body text-primary underline underline-offset-4">Go to Home</RouterLink>
  </main>
</template>
