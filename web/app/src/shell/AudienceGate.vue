<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { useHasRole } from '@/auth/useHasRole';
import { Skeleton } from '@/components/ui/skeleton';
import { canReach, moduleOf } from './audience';
import { useCurrentPharmacy } from './useCurrentPharmacy';

// The page at the address, if it is for the user; otherwise why not, and the way home. Hiding is a convenience, not
// protection: the backend's policies decide. While the memberships load a pharmacy's page is a skeleton, so neither
// the page nor "Not available" flashes.
const route = useRoute();
const { status, memberships, current } = useCurrentPharmacy();
const isEditor = useHasRole('editor');

const module = computed(() => moduleOf(route.path));

const state = computed<'page' | 'loading' | 'refused'>(() => {
  if (module.value === undefined) {
    return 'page';
  }
  if (canReach(module.value.audience, { current: current.value, isEditor: isEditor.value })) {
    return 'page';
  }
  return module.value.audience !== 'editor' && status.value === 'loading' ? 'loading' : 'refused';
});

const reason = computed(() => {
  if (module.value?.audience === 'editor') {
    return 'This page is not available to you.';
  }
  if (status.value === 'failed') {
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
  <div v-else-if="state === 'loading'" class="flex flex-col gap-3 p-4" aria-busy="true">
    <Skeleton class="h-8 w-1/3" />
    <Skeleton class="h-4 w-2/3" />
    <Skeleton class="h-4 w-1/2" />
  </div>
  <main v-else class="flex flex-col items-start gap-2 p-4">
    <h1 class="text-title">Not available</h1>
    <p class="text-body">{{ reason }}</p>
    <RouterLink to="/" class="text-body text-primary underline underline-offset-4">Go to Home</RouterLink>
  </main>
</template>
