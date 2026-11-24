<script setup lang="ts">
import { LogOutIcon } from '@lucide/vue';
import { useI18n } from 'vue-i18n';
import { useCurrentUser } from '@/api/useCurrentUser';
import { Button } from '@/components/ui/button';
import { useAuth } from '@/auth/useAuth';
import LanguageSwitch from '@/shell/LanguageSwitch.vue';

// The app shell's user menu: who is signed in, as the host's /me names them, the language, and Sign out.
const { signoutRedirect } = useAuth();
const name = useCurrentUser();
const { t } = useI18n();
</script>

<template>
  <div class="flex flex-col gap-2 px-2 text-sm">
    <div class="flex items-center justify-between gap-2">
      <span class="truncate">{{ name === undefined ? t('auth.signedIn') : t('auth.signedInAs', { name }) }}</span>
      <Button variant="ghost" size="sm" @click="signoutRedirect()">
        <LogOutIcon />
        {{ t('auth.signOut') }}
      </Button>
    </div>
    <LanguageSwitch />
  </div>
</template>
