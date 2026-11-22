<script setup lang="ts">
import { useI18n } from 'vue-i18n';
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
const { t } = useI18n();

/** Whether the address is the page's or below it. */
const isAt = (path: string) => route.path === path || route.path.startsWith(`${path}/`);

/** Closes the sheet once a link is tapped, also the link of the page the user is already on. */
function closeSheet() {
  if (isMobile.value) {
    setOpenMobile(false);
  }
}
</script>

<template>
  <SidebarGroup>
    <SidebarGroupLabel>{{ t('shell.navigation.label') }}</SidebarGroupLabel>
    <SidebarGroupContent>
      <nav :aria-label="t('shell.navigation.label')">
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton as-child :is-active="route.path === '/'">
              <RouterLink to="/" @click="closeSheet">{{ t('shell.home') }}</RouterLink>
            </SidebarMenuButton>
          </SidebarMenuItem>
          <SidebarMenuItem v-for="module in modules" :key="module.path">
            <SidebarMenuButton as-child :is-active="isAt(`/${module.path}`)">
              <RouterLink :to="`/${module.path}`" @click="closeSheet">{{ t(module.titleKey) }}</RouterLink>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </nav>
    </SidebarGroupContent>
  </SidebarGroup>
</template>
