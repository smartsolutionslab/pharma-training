<script setup lang="ts">
import { watch } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import {
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  useSidebar,
} from '@/components/ui/sidebar';
import { useVisibleModules } from './useVisibleModules';

// The sidebar's navigation: the start page and every module the user may see. Below the tablet's width the sidebar is
// a sheet, which closes once the user has gone to a page.
const route = useRoute();
const modules = useVisibleModules();
const { isMobile, setOpenMobile } = useSidebar();

/** Whether the address is the page's or below it. */
const isAt = (path: string) => route.path === path || route.path.startsWith(`${path}/`);

watch(
  () => route.fullPath,
  () => {
    if (isMobile.value) {
      setOpenMobile(false);
    }
  },
);
</script>

<template>
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
          <SidebarMenuItem v-for="module in modules" :key="module.path">
            <SidebarMenuButton as-child :is-active="isAt(`/${module.path}`)">
              <RouterLink :to="`/${module.path}`">{{ module.title }}</RouterLink>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </nav>
    </SidebarGroupContent>
  </SidebarGroup>
</template>
