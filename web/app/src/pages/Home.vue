<script setup lang="ts">
import { computed, useId } from 'vue';
import { Button } from '@/components/ui/button';
import { useCurrentPharmacy } from '@/shell/useCurrentPharmacy';

defineOptions({ name: 'HomePage' });

// With several pharmacies and none chosen, the start page asks the user to choose the one they work in.
const { status, memberships, current, choose } = useCurrentPharmacy();
const asksForPharmacy = computed(
  () => status.value === 'ready' && memberships.value.length > 1 && current.value === undefined,
);
const headingId = useId();
</script>

<template>
  <main>
    <h1>pharma-training</h1>
    <section v-if="asksForPharmacy" :aria-labelledby="headingId" class="flex flex-col gap-2 p-4">
      <h2 :id="headingId" class="text-title">Choose your pharmacy</h2>
      <ul class="flex flex-col gap-2">
        <li v-for="membership in memberships" :key="membership.id">
          <Button variant="outline" class="h-auto flex-col items-start" @click="choose(membership.id)">
            <span class="text-body font-medium">{{ membership.name }}</span>
            <span class="text-caption text-muted-foreground">{{ membership.role }}</span>
          </Button>
        </li>
      </ul>
    </section>
  </main>
</template>
