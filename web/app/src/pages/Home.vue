<script setup lang="ts">
import { computed, nextTick, ref, useId } from 'vue';
import { Button } from '@/components/ui/button';
import { useCurrentPharmacy } from '@/shell/useCurrentPharmacy';

defineOptions({ name: 'HomePage' });

// With several pharmacies and none chosen, the start page asks the user to choose the one they work in.
const { status, memberships, current, choose } = useCurrentPharmacy();
const asksForPharmacy = computed(
  () => status.value === 'ready' && memberships.value.length > 1 && current.value === undefined,
);
const headingId = useId();
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
  </main>
</template>
