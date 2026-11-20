import type { User } from 'oidc-client-ts';
import { computed, type Plugin } from 'vue';
import { type Auth, authKey } from './useAuth';

// The local user developer as signed in, with the ID token it has locally.
const developer: Auth = {
  user: computed(
    () =>
      ({
        profile: {
          preferred_username: 'developer',
          realm_access: { roles: ['manager'] },
        },
      }) as unknown as User,
  ),
  isAuthenticated: computed(() => true),
  isLoading: computed(() => false),
  error: computed(() => null),
  activeNavigator: computed(() => null),
  signinRedirect: () => Promise.resolve(),
  signoutRedirect: () => Promise.resolve(),
};

/**
 * Installs the local user developer, signed in, in place of withAuth: for a test that renders a screen on its own, as
 * each screen's axe check does.
 */
export const signedInAsDeveloper: Plugin = {
  install(app) {
    app.provide(authKey, developer);
  },
};
