<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb';
import { useVisibleModules, useVisiblePages } from './useVisibleModules';

// One word is the file's name; lint wants a component's name in two.
defineOptions({ name: 'ShellBreadcrumbs' });

interface Crumb {
  label: string;
  to: string;
}

// Home, the module the address is in (by the title its routes.ts gives it), and each step of the address below it.
const route = useRoute();
// A module the user may not see is an unknown segment of the address to them: it is not named.
const modules = useVisibleModules();
// A page below a module is named by its title where the user may see it, whether or not they may see its module.
const pages = useVisiblePages();
const crumbs = computed<Crumb[]>(() => {
  const [first, ...rest] = route.path.split('/').filter((segment) => segment !== '');
  const all: Crumb[] = [{ label: 'Home', to: '/' }];
  if (first === undefined) {
    return all;
  }

  const module = modules.value.find((candidate) => candidate.path === first);
  all.push({ label: module?.title ?? decodeURIComponent(first), to: `/${first}` });
  let to = `/${first}`;
  for (const segment of rest) {
    to = `${to}/${segment}`;
    all.push({ label: pages.value.find((page) => page.to === to)?.title ?? decodeURIComponent(segment), to });
  }
  return all;
});
</script>

<!-- Where the user is, from the route hierarchy: every step a link but the current one. -->
<template>
  <Breadcrumb>
    <BreadcrumbList>
      <template v-for="(crumb, index) in crumbs" :key="crumb.to">
        <BreadcrumbSeparator v-if="index > 0" />
        <BreadcrumbItem>
          <BreadcrumbPage v-if="index === crumbs.length - 1">{{ crumb.label }}</BreadcrumbPage>
          <BreadcrumbLink v-else as-child>
            <RouterLink :to="crumb.to">{{ crumb.label }}</RouterLink>
          </BreadcrumbLink>
        </BreadcrumbItem>
      </template>
    </BreadcrumbList>
  </Breadcrumb>
</template>
