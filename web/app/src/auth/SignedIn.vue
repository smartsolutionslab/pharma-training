<script setup lang="ts">
import { inject, shallowRef, watchEffect } from 'vue';
import { useI18n } from 'vue-i18n';
import { routerKey } from 'vue-router';
import { useAuth } from '@/auth/useAuth';

/**
 * Renders its default slot for a signed-in user and sends everyone else to the identity provider. Who is signed in, and
 * Sign out, are in the app shell's user menu (UserMenu.vue). With a router installed, no sign-in starts before its
 * first navigation has settled: its guards take what must not leave the app (an invitation link's token) out of the
 * address first. Without one it signs in at once.
 */
const { isAuthenticated, isLoading, error, activeNavigator, signinRedirect } = useAuth();
const { t } = useI18n();

const router = inject(routerKey, null);
const routerReady = shallowRef(router === null);
if (router !== null) {
  // A first navigation that fails has still settled: the sign-in goes ahead, from whatever address the router left.
  void router
    .isReady()
    .catch(() => undefined)
    .then(() => {
      routerReady.value = true;
    });
}

watchEffect(() => {
  if (routerReady.value && !isAuthenticated.value && !isLoading.value && !error.value && !activeNavigator.value) {
    void signinRedirect();
  }
});
</script>

<template>
  <p v-if="error" role="alert">{{ t('auth.signInFailed', { message: error.message }) }}</p>
  <p v-else-if="activeNavigator === 'signoutRedirect'">{{ t('auth.signingOut') }}</p>
  <p v-else-if="!isAuthenticated">{{ t('auth.signingIn') }}</p>
  <slot v-else />
</template>
