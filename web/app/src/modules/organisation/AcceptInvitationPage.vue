<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink } from 'vue-router';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useAcceptInvitation } from './useAcceptInvitation';

// The invitation mail's link: the invitation is accepted as the page opens, with the token its route kept for this
// tab. Who accepts comes from the access token; the page never shows the token.
const { t } = useI18n();
const { state, retry } = useAcceptInvitation();

const retryable = computed(() => state.value.kind === 'refused' && state.value.refusal === 'failed');
</script>

<!-- One landmark through every state: what the user reads in it changes, the page's main does not. -->
<template>
  <main
    class="flex max-w-xl flex-col items-start gap-2 p-4"
    :aria-busy="state.kind === 'accepting' ? 'true' : undefined"
  >
    <template v-if="state.kind === 'accepting'">
      <h1 class="text-title">{{ t('organisation.accept.title') }}</h1>
      <span class="sr-only">{{ t('organisation.accept.accepting') }}</span>
      <Skeleton class="h-4 w-2/3" />
      <Skeleton class="h-4 w-1/2" />
    </template>
    <template v-else>
      <h1 class="text-title">{{ t(`organisation.accept.${state.refusal}.title`) }}</h1>
      <p class="text-body">{{ t(`organisation.accept.${state.refusal}.text`) }}</p>
      <Button v-if="retryable" variant="outline" @click="retry">{{ t('shell.retry') }}</Button>
      <RouterLink to="/" class="text-body text-primary underline underline-offset-4">{{
        t('shell.gate.goHome')
      }}</RouterLink>
    </template>
  </main>
</template>
