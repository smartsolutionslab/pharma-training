<script setup lang="ts">
import { computed, nextTick, ref, useId } from 'vue';
import { RouterLink } from 'vue-router';
import { Button } from '@/components/ui/button';
import { useCurrentPharmacy } from '@/shell/useCurrentPharmacy';

defineOptions({ name: 'HomePage' });

// With several pharmacies and none chosen, the start page asks the user to choose the one they work in.
const { status, memberships, current, choose } = useCurrentPharmacy();
const asksForPharmacy = computed(
  () => status.value === 'ready' && memberships.value.length > 1 && current.value === undefined,
);
// With none at all, it leads the user to register their pharmacy, or to ask its manager for an invitation.
const hasNoPharmacy = computed(() => status.value === 'ready' && memberships.value.length === 0);
const headingId = useId();
const noPharmacyHeadingId = useId();
const title = ref<HTMLHeadingElement | null>(null);

// The chosen button goes away with the prompt: focus moves to the page's heading rather than falling to the body.
async function chooseAndFocus(id: string) {
  choose(id);
  await nextTick();
  title.value?.focus();
}
</script>

<template>
  <main>
    <h1 ref="title" tabindex="-1" class="text-title focus:outline-none">pharma-training</h1>
    <section v-if="asksForPharmacy" :aria-labelledby="headingId" class="flex flex-col gap-2 p-4">
      <h2 :id="headingId" class="text-body font-semibold">Choose your pharmacy</h2>
      <ul class="flex flex-col gap-2">
        <li v-for="membership in memberships" :key="membership.id">
          <Button variant="outline" class="h-auto flex-col items-start" @click="chooseAndFocus(membership.id)">
            <span class="text-body font-medium">{{ membership.name }}</span>
            <span class="text-caption text-muted-foreground">{{ membership.role }}</span>
          </Button>
        </li>
      </ul>
    </section>
    <section v-if="hasNoPharmacy" :aria-labelledby="noPharmacyHeadingId" class="flex flex-col items-start gap-2 p-4">
      <h2 :id="noPharmacyHeadingId" class="text-body font-semibold">No pharmacy yet</h2>
      <p class="text-body">
        You are not a member of any pharmacy yet. Register your pharmacy, or ask its manager for an invitation.
      </p>
      <RouterLink to="/organisation/register" class="text-body text-primary underline underline-offset-4">
        Register a pharmacy
      </RouterLink>
    </section>
  </main>
</template>
