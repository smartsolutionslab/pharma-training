<script setup lang="ts">
import { LogOutIcon } from '@lucide/vue';
import { useI18n } from 'vue-i18n';
import { useCurrentUser } from '@/api/useCurrentUser';
import { Button } from '@/components/ui/button';
import { useAuth } from '@/auth/useAuth';
import { type Language, languages, type MessageKey, setLanguage } from '@/i18n';

// The app shell's user menu: who is signed in, as the host's /me names them, the language, and Sign out.
const { signoutRedirect } = useAuth();
const name = useCurrentUser();
const { t, locale } = useI18n();

// Each language is named in its own words, the same in both catalogs.
const labels: Record<Language, MessageKey> = { 'de-DE': 'language.de', 'en-GB': 'language.en' };
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
    <div role="group" :aria-label="t('language.label')" class="flex gap-1">
      <Button
        v-for="language in languages"
        :key="language"
        :variant="language === locale ? 'secondary' : 'ghost'"
        size="sm"
        :lang="language"
        :aria-pressed="language === locale"
        @click="setLanguage(language)"
      >
        {{ t(labels[language]) }}
      </Button>
    </div>
  </div>
</template>
