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
import { moduleRoutes } from '@/modules';
import { useVisibleModules, useVisiblePages } from './useVisibleModules';

// One word is the file's name; lint wants a component's name in two.
defineOptions({ name: 'ShellBreadcrumbs' });

interface Crumb {
  label: string;
  to: string;
  /** False for a step the user may not open: it is named, but leads nowhere. */
  link: boolean;
}

// Home, the module the address is in (by the title its routes.ts gives it), and each step of the address below it.
const route = useRoute();
// A module the user may not see is an unknown segment of the address to them: it is not named.
const modules = useVisibleModules();
// A page below a module is named by its title where the user may see it, whether or not they may see its module. Its
// module, hidden from the user, is then named by its title too, as text: a link would lead to a page refused to them.
const pages = useVisiblePages();
const crumbs = computed<Crumb[]>(() => {
  const [first, ...rest] = route.path.split('/').filter((segment) => segment !== '');
  const all: Crumb[] = [{ label: 'Home', to: '/', link: true }];
  if (first === undefined) {
    return all;
  }

  const module = modules.value.find((candidate) => candidate.path === first);
  const page = rest.length > 0 ? pages.value.find((candidate) => candidate.to === `/${first}/${rest[0]}`) : undefined;
  const hiddenModule = page === undefined ? undefined : moduleRoutes.find((candidate) => candidate.path === first);
  if (module !== undefined) {
    all.push({ label: module.title, to: `/${first}`, link: true });
  } else if (hiddenModule !== undefined) {
    all.push({ label: hiddenModule.title, to: `/${first}`, link: false });
  } else {
    all.push({ label: decodeURIComponent(first), to: `/${first}`, link: true });
  }
  let to = `/${first}`;
  for (const segment of rest) {
    to = `${to}/${segment}`;
    all.push({
      label: pages.value.find((candidate) => candidate.to === to)?.title ?? decodeURIComponent(segment),
      to,
      link: true,
    });
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
          <span v-else-if="!crumb.link" class="text-muted-foreground">{{ crumb.label }}</span>
          <BreadcrumbLink v-else as-child>
            <RouterLink :to="crumb.to">{{ crumb.label }}</RouterLink>
          </BreadcrumbLink>
        </BreadcrumbItem>
      </template>
    </BreadcrumbList>
  </Breadcrumb>
</template>
