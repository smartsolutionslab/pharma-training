import { render, screen } from '@testing-library/vue';
import type { User } from 'oidc-client-ts';
import { expect, test } from 'vitest';
import { computed, createApp } from 'vue';
import RequirePermission from './RequirePermission.vue';
import { type Auth, authKey } from './useAuth';
import { useHasRole } from './useHasRole';

// The ID tokens Keycloak issues the realm's local users (src/AppHost/realms/realm.json); a user without a realm role has
// no realm_access, one without a department no department.
const users = {
  developer: {
    preferred_username: 'developer',
    realm_access: { roles: ['manager'] },
  },
  visitor: {
    preferred_username: 'visitor',
  },
};

type Name = keyof typeof users;

/** useAuth() as the composables see it: a signed-in user whose ID token has the claims above. */
function signedInAs(user: Name): Auth {
  return { user: computed(() => ({ profile: users[user] }) as unknown as User) } as unknown as Auth;
}

/** Runs a composable as a component's setup would, with useAuth() answering for the user. */
function asUser<T>(user: Name, composable: () => T): T {
  const app = createApp({});
  app.provide(authKey, signedInAs(user));
  return app.runWithContext(composable);
}

function renderAs(user: Name, props: InstanceType<typeof RequirePermission>['$props'], slots: Record<string, string>) {
  return render(RequirePermission, { props, slots, global: { provide: { [authKey]: signedInAs(user) } } });
}

test('useHasRole answers true for the developer and false for the visitor', () => {
  expect(asUser('developer', () => useHasRole('manager')).value).toBe(true);
  expect(asUser('visitor', () => useHasRole('manager')).value).toBe(false);
});

test('an element that requires the manager role is shown to the developer', () => {
  renderAs('developer', { role: 'manager' }, { default: '<button type="button">Manage</button>' });

  expect(screen.getByRole('button', { name: 'Manage' })).toBeInTheDocument();
});

test('an element that requires the manager role is hidden from the visitor', () => {
  renderAs('visitor', { role: 'manager' }, { default: '<button type="button">Manage</button>' });

  expect(screen.queryByRole('button', { name: 'Manage' })).not.toBeInTheDocument();
});

test('the visitor sees the fallback instead', () => {
  renderAs(
    'visitor',
    { role: 'manager' },
    { default: '<button type="button">Manage</button>', fallback: '<p>Ask a manager.</p>' },
  );

  expect(screen.getByText('Ask a manager.')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Manage' })).not.toBeInTheDocument();
});
