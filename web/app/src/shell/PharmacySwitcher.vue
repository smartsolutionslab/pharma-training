<script setup lang="ts">
import { ChevronsUpDownIcon } from '@lucide/vue';
import { computed, useId } from 'vue';
import { RouterLink } from 'vue-router';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { useSidebar } from '@/components/ui/sidebar';
import { Skeleton } from '@/components/ui/skeleton';
import { useCurrentPharmacy } from './useCurrentPharmacy';
import { useMyPharmacies } from './useMyPharmacies';

// The pharmacy the user works in, in the sidebar's header: with several, a menu to switch between them; with one, its
// name; with none, a sentence saying so. Once they are known, a link to register another: below the tablet's width
// the sidebar is a sheet, which closes once the link is followed.
const { status, memberships, current, choose } = useCurrentPharmacy();
const { refetch } = useMyPharmacies();
const { isMobile, setOpenMobile } = useSidebar();

function closeSheet() {
  if (isMobile.value) {
    setOpenMobile(false);
  }
}

const labelId = useId();
const trigger = computed(() => (current.value === undefined ? 'Choose a pharmacy' : `Pharmacy: ${current.value.name}`));

function pick(id: unknown) {
  if (typeof id === 'string') {
    choose(id);
  }
}
</script>

<!-- A region of its own, which the open menu stays inside, so screen readers find it in a landmark. -->
<template>
  <section aria-label="Pharmacy" class="px-2">
    <Skeleton v-if="status === 'loading'" class="h-10 w-full" />
    <div v-else-if="status === 'failed'" class="flex flex-col items-start gap-1 text-caption">
      <p role="alert">Pharmacies could not be loaded.</p>
      <Button variant="outline" size="sm" @click="refetch()">Try again</Button>
    </div>
    <p v-else-if="memberships.length === 0" class="text-caption text-muted-foreground">
      You are not a member of any pharmacy yet.
    </p>
    <p v-else-if="memberships.length === 1 && current !== undefined" class="flex flex-col">
      <span class="truncate text-body font-medium">{{ current.name }}</span>
      <span class="text-caption text-muted-foreground">{{ current.role }}</span>
    </p>
    <DropdownMenu v-else>
      <DropdownMenuTrigger as-child>
        <Button variant="ghost" class="h-auto w-full justify-between px-2 py-1.5 text-left" :aria-label="trigger">
          <span v-if="current === undefined" class="text-body">Choose a pharmacy</span>
          <span v-else class="flex min-w-0 flex-col">
            <span class="truncate text-body font-medium">{{ current.name }}</span>
            <span class="text-caption text-muted-foreground">{{ current.role }}</span>
          </span>
          <ChevronsUpDownIcon aria-hidden="true" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent
        inline
        align="start"
        class="w-(--reka-dropdown-menu-trigger-width)"
        :aria-labelledby="labelId"
      >
        <DropdownMenuLabel :id="labelId">Pharmacies</DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuRadioGroup :model-value="current?.id" @update:model-value="pick">
          <DropdownMenuRadioItem v-for="membership in memberships" :key="membership.id" :value="membership.id">
            <span class="flex min-w-0 flex-col">
              <span class="truncate">{{ membership.name }}</span>
              <span class="text-caption text-muted-foreground">{{ membership.role }}</span>
            </span>
          </DropdownMenuRadioItem>
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
    <RouterLink
      v-if="status === 'ready'"
      to="/organisation/register"
      class="mt-1 inline-block text-caption text-primary underline underline-offset-4"
      @click="closeSheet"
    >
      Register a pharmacy
    </RouterLink>
  </section>
</template>
