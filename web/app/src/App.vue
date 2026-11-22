<script setup lang="ts">
import { useI18n } from 'vue-i18n';
import { RouterView } from 'vue-router';
import { Separator } from '@/components/ui/separator';
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarHeader,
  SidebarInset,
  SidebarProvider,
  SidebarRail,
  SidebarTrigger,
} from '@/components/ui/sidebar';
import { Toaster } from '@/components/ui/sonner';
import AudienceGate from '@/shell/AudienceGate.vue';
import Breadcrumbs from '@/shell/Breadcrumbs.vue';
import CommandPalette from '@/shell/CommandPalette.vue';
import PharmacySwitcher from '@/shell/PharmacySwitcher.vue';
import ShellNavigation from '@/shell/ShellNavigation.vue';
import ThemeToggle from '@/theme/ThemeToggle.vue';
import UserMenu from '@/auth/UserMenu.vue';

// The app shell: a sidebar with the pharmacy switcher and every module's page the user may see (each module's routes.ts
// adds its own), the breadcrumbs and the command palette above the page, the page if it is for the user, and the toasts.
const { t } = useI18n();
</script>

<template>
  <SidebarProvider>
    <Sidebar>
      <SidebarHeader>
        <p class="px-2 py-1 font-semibold">pharma-training</p>
        <PharmacySwitcher />
      </SidebarHeader>
      <SidebarContent>
        <ShellNavigation />
      </SidebarContent>
      <SidebarFooter>
        <ThemeToggle />
        <UserMenu />
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
    <SidebarInset>
      <header class="flex h-12 shrink-0 items-center gap-2 border-b bg-glass px-4 backdrop-blur-md">
        <SidebarTrigger class="-ml-1" />
        <Separator orientation="vertical" class="mr-2 data-[orientation=vertical]:h-4" />
        <Breadcrumbs />
        <CommandPalette />
      </header>
      <AudienceGate>
        <RouterView />
      </AudienceGate>
    </SidebarInset>
    <Toaster :container-aria-label="t('toasts.region')" :toast-options="{ closeButtonAriaLabel: t('toasts.close') }" />
  </SidebarProvider>
</template>
