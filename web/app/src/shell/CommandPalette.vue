<script setup lang="ts">
import { SearchIcon } from '@lucide/vue';
import { onBeforeUnmount, onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { Button } from '@/components/ui/button';
import {
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandShortcut,
} from '@/components/ui/command';
import { useVisibleModules, useVisiblePages } from './useVisibleModules';

// Goes to any page the user may see (Home, the modules, then the pages below them) from the keyboard: Ctrl+K (Cmd+K on a Mac) opens it, typing narrows the pages, the arrow keys pick
// one and Enter goes there. The button beside the breadcrumbs opens it too.
const open = ref(false);
const router = useRouter();
const modules = useVisibleModules();
const pages = useVisiblePages();

function onKeyDown(event: KeyboardEvent) {
  if (event.key.toLowerCase() === 'k' && (event.metaKey || event.ctrlKey)) {
    event.preventDefault();
    open.value = !open.value;
  }
}

onMounted(() => window.addEventListener('keydown', onKeyDown));
onBeforeUnmount(() => window.removeEventListener('keydown', onKeyDown));

function go(to: string) {
  open.value = false;
  void router.push(to);
}
</script>

<template>
  <Button
    variant="outline"
    size="sm"
    class="ml-auto text-muted-foreground"
    aria-keyshortcuts="Control+K Meta+K"
    @click="open = true"
  >
    <SearchIcon />
    Go to…
    <CommandShortcut>Ctrl K</CommandShortcut>
  </Button>
  <CommandDialog v-model:open="open" title="Go to" description="Pick a page to go to.">
    <CommandInput placeholder="Go to…" aria-label="Go to" />
    <CommandList aria-label="Pages">
      <CommandEmpty>No page matches.</CommandEmpty>
      <CommandGroup heading="Pages">
        <CommandItem value="Home" @select="go('/')">Home</CommandItem>
        <CommandItem v-for="route in modules" :key="route.path" :value="route.title" @select="go(`/${route.path}`)">
          {{ route.title }}
        </CommandItem>
        <CommandItem v-for="page in pages" :key="page.to" :value="page.title" @select="go(page.to)">
          {{ page.title }}
        </CommandItem>
      </CommandGroup>
    </CommandList>
  </CommandDialog>
</template>
