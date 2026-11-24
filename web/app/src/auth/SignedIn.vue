<script setup lang="ts">
import { computed, inject, shallowRef, watchEffect } from 'vue';
import { useI18n } from 'vue-i18n';
import { routerKey, START_LOCATION } from 'vue-router';
import { useAuth } from '@/auth/useAuth';
import { isPublicAddress } from '@/shell/audience';

/**
 * Renders its default slot for a signed-in user and sends everyone else to the identity provider. Who is signed in, and
 * Sign out, are in the app shell's user menu (UserMenu.vue). With a router installed, nothing renders and no sign-in
 * starts before its first navigation has settled: its guards take what must not leave the app (an invitation link's
 * token) out of the address first, and only then is it known whether the address is public (a certificate's
 * verification page, #54), which renders for anyone, signed in or not, without a sign-in. Without a router it signs in
 * at once.
 */
const { isAuthenticated, isLoading, error, activeNavigator, signinRedirect } = useAuth();
const { t } = useI18n();

const router = inject(routerKey, null);
const routerReady = shallowRef(router === null || router.currentRoute.value !== START_LOCATION);
if (router !== null && !routerReady.value) {
  // A first navigation that fails has still settled: the sign-in goes ahead, from whatever address the router left.
  void router
    .isReady()
    .catch(() => undefined)
    .then(() => {
      routerReady.value = true;
    });
}

// Read again on every navigation: leaving a public page for a page of the app (typed by hand, Back) signs in.
const isPublic = computed(
  () => routerReady.value && router !== null && isPublicAddress(router.currentRoute.value.path),
);

watchEffect(() => {
  if (
    routerReady.value &&
    !isPublic.value &&
    !isAuthenticated.value &&
    !isLoading.value &&
    !error.value &&
    !activeNavigator.value
  ) {
    void signinRedirect();
  }
});
</script>

<template>
  <template v-if="routerReady">
    <slot v-if="isPublic" />
    <p v-else-if="error" role="alert">{{ t('auth.signInFailed', { message: error.message }) }}</p>
    <p v-else-if="activeNavigator === 'signoutRedirect'">{{ t('auth.signingOut') }}</p>
    <p v-else-if="!isAuthenticated">{{ t('auth.signingIn') }}</p>
    <slot v-else />
  </template>
</template>
