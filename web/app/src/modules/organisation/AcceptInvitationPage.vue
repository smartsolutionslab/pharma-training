<script setup lang="ts">
import { computed, nextTick, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { onBeforeRouteUpdate, RouterLink } from 'vue-router';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { messageText } from '@/i18n/errorText';
import { captureFrom } from './invitationLink';
import { useAcceptInvitation } from './useAcceptInvitation';

// The invitation mail's link: the page shows the token its route kept for this tab ready to accept, and accepts it
// only when the user presses the button. It names no pharmacy before that. Who accepts comes from the access token;
// the page never shows the token.
const { t } = useI18n();
const { state, confirm, retry, start } = useAcceptInvitation();

// A second link opened while the page is shown changes only the address's fragment: the route's beforeEnter does not
// run, so the page takes the token out of the address itself and shows it ready to accept. The address without the
// fragment is the one already shown, so the redirect may end as a duplicate navigation; the fragment is gone either
// way.
onBeforeRouteUpdate((to) => {
  const answer = captureFrom(to);
  if (answer !== true) {
    start();
  }
  return answer;
});

// Retry only where trying again can succeed (too many attempts, a lost concurrent save, the host unavailable, the
// network); every other refusal is final.
const retryable = computed(() => state.value.kind === 'refused' && state.value.retryable === true);

// What the page says for a refusal: a Retry answered not found may follow a first attempt that was accepted, and an
// invitation changed by another save has words of its own under the failed title.
const title = computed(() =>
  state.value.kind !== 'refused' ||
  state.value.refusal === 'notFoundAfterRetry' ||
  state.value.refusal === 'invitationChanged'
    ? t('organisation.accept.failed.title')
    : t(`organisation.accept.${state.value.refusal}.title`),
);
const text = computed(() => {
  if (state.value.kind !== 'refused') {
    return '';
  }
  if (state.value.refusal === 'notFoundAfterRetry') {
    return t('organisation.accept.retryNotFound');
  }
  if (state.value.refusal === 'invitationChanged') {
    return t('organisation.accept.invitationChanged');
  }
  return state.value.message === undefined
    ? t(`organisation.accept.${state.value.refusal}.text`)
    : messageText(state.value.message);
});

// A member already goes on to their courses, and so may someone whose Retry found the invitation used; everyone else
// home.
const ways = computed(() => {
  const courses = { to: '/learning', label: t('organisation.accept.goToCourses') };
  const home = { to: '/', label: t('shell.gate.goHome') };
  if (state.value.kind !== 'refused') {
    return [];
  }
  if (state.value.refusal === 'alreadyMember') {
    return [courses];
  }
  if (state.value.refusal === 'notFoundAfterRetry') {
    return [courses, home];
  }
  return [home];
});

// The page's heading takes focus as the page opens and with every change of state after, so a screen reader announces
// it; the Accept button is never focused for the user, so no key pressed by chance accepts.
const heading = useTemplateRef<HTMLHeadingElement>('heading');
watch(
  state,
  async () => {
    await nextTick();
    heading.value?.focus();
  },
  { immediate: true },
);
</script>

<!-- One landmark through every state: what the user reads in it changes, the page's main does not. -->
<template>
  <main
    class="flex max-w-xl flex-col items-start gap-2 p-4"
    :aria-busy="state.kind === 'accepting' ? 'true' : undefined"
  >
    <template v-if="state.kind === 'ready'">
      <h1 ref="heading" tabindex="-1" class="text-title focus:outline-none">
        {{ t('organisation.accept.ready.title') }}
      </h1>
      <p class="text-body">{{ t('organisation.accept.ready.text') }}</p>
      <Button @click="confirm">{{ t('organisation.accept.ready.accept') }}</Button>
    </template>
    <template v-else-if="state.kind === 'accepting'">
      <!-- Its own words, not the button's again, so a screen reader announces what the click started. -->
      <h1 ref="heading" tabindex="-1" class="text-title focus:outline-none">
        {{ t('organisation.accept.accepting.title') }}
      </h1>
      <Skeleton class="h-4 w-2/3" />
      <Skeleton class="h-4 w-1/2" />
    </template>
    <template v-else>
      <h1 ref="heading" tabindex="-1" class="text-title focus:outline-none">{{ title }}</h1>
      <p class="text-body">{{ text }}</p>
      <Button v-if="retryable" variant="outline" @click="retry">{{ t('shell.retry') }}</Button>
      <RouterLink
        v-for="way in ways"
        :key="way.to"
        :to="way.to"
        class="text-body text-primary underline underline-offset-4"
        >{{ way.label }}</RouterLink
      >
    </template>
  </main>
</template>
