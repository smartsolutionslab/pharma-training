<script setup lang="ts">
import { RouterLink, RouterView, useRoute } from 'vue-router';
import { Separator } from '@/components/ui/separator';
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarInset,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  SidebarRail,
  SidebarTrigger,
} from '@/components/ui/sidebar';
import { Toaster } from '@/components/ui/sonner';
import { moduleRoutes } from '@/modules';
import Breadcrumbs from '@/shell/Breadcrumbs.vue';
import CommandPalette from '@/shell/CommandPalette.vue';
import ThemeToggle from '@/theme/ThemeToggle.vue';
import UserMenu from '@/auth/UserMenu.vue';

// The app shell: a sidebar with every module's page (each module's routes.ts adds its own), the breadcrumbs and the
// command palette above the page, and the toasts.
const route = useRoute();

/** Whether the address is the page's or below it. */
const isAt = (path: string) => route.path === path || route.path.startsWith(`${path}/`);
</script>

<template>
  <SidebarProvider>
    <Sidebar>
      <SidebarHeader>
        <p class="px-2 py-1 font-semibold">pharma-training</p>
      </SidebarHeader>
      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupLabel>Modules</SidebarGroupLabel>
          <SidebarGroupContent>
            <nav aria-label="Modules">
              <SidebarMenu>
                <SidebarMenuItem>
                  <SidebarMenuButton as-child :is-active="route.path === '/'">
                    <RouterLink to="/">Home</RouterLink>
                  </SidebarMenuButton>
                </SidebarMenuItem>
                <SidebarMenuItem v-for="module in moduleRoutes" :key="module.path">
                  <SidebarMenuButton as-child :is-active="isAt(`/${module.path}`)">
                    <RouterLink :to="`/${module.path}`">{{ module.title }}</RouterLink>
                  </SidebarMenuButton>
                </SidebarMenuItem>
              </SidebarMenu>
            </nav>
          </SidebarGroupContent>
        </SidebarGroup>
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
      <RouterView />
    </SidebarInset>
    <Toaster />
  </SidebarProvider>
</template>
