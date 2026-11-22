<script setup lang="ts">
import { SearchIcon } from '@lucide/vue';
import { onBeforeUnmount, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
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
const { t } = useI18n();

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
    {{ t('shell.palette.open') }}
    <CommandShortcut>{{ t('shell.palette.shortcut') }}</CommandShortcut>
  </Button>
  <CommandDialog v-model:open="open" :title="t('shell.palette.title')" :description="t('shell.palette.description')">
    <CommandInput :placeholder="t('shell.palette.open')" :aria-label="t('shell.palette.title')" />
    <CommandList :aria-label="t('shell.palette.pages')">
      <CommandEmpty>{{ t('shell.palette.empty') }}</CommandEmpty>
      <CommandGroup :heading="t('shell.palette.pages')">
        <CommandItem :value="t('shell.home')" @select="go('/')">{{ t('shell.home') }}</CommandItem>
        <CommandItem
          v-for="route in modules"
          :key="route.path"
          :value="t(route.titleKey)"
          @select="go(`/${route.path}`)"
        >
          {{ t(route.titleKey) }}
        </CommandItem>
        <CommandItem v-for="page in pages" :key="page.to" :value="t(page.titleKey)" @select="go(page.to)">
          {{ t(page.titleKey) }}
        </CommandItem>
      </CommandGroup>
    </CommandList>
  </CommandDialog>
</template>
