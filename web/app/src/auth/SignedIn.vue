<script setup lang="ts">
import { watchEffect } from 'vue';
import { useI18n } from 'vue-i18n';
import { useAuth } from '@/auth/useAuth';

/**
 * Renders its default slot for a signed-in user and sends everyone else to the identity provider. Who is signed in, and
 * Sign out, are in the app shell's user menu (UserMenu.vue).
 */
const { isAuthenticated, isLoading, error, activeNavigator, signinRedirect } = useAuth();
const { t } = useI18n();

watchEffect(() => {
  if (!isAuthenticated.value && !isLoading.value && !error.value && !activeNavigator.value) {
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
