import { UserManager } from 'oidc-client-ts';
import { type App, type Component, h } from 'vue';
import { oidcSettings } from '@/auth/oidcConfig';
import { runtimeConfig } from '@/auth/runtimeConfig';
import SignedIn from '@/auth/SignedIn.vue';
import { authKey, createAuth } from '@/auth/useAuth';

/** Signs the user in through the identity provider the edge named at runtime, and renders the app inside SignedIn. */
export const withAuth = (app: App, root: Component): Component => {
  app.provide(authKey, createAuth(new UserManager(oidcSettings(runtimeConfig()))));
  return { render: () => h(SignedIn, null, { default: () => h(root) }) };
};
