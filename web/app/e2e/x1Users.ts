import { randomBytes } from 'node:crypto';
import { type APIRequestContext, expect } from '@playwright/test';
import { unique } from './invitations';

// The users the MVP 1 flow (X1, #55) signs in as. In run mode they are the realm's local users, whose password is their
// name. Against staging (#130) there are no local users: the test creates its own at set-up through Keycloak's admin API,
// as the staging-only confidential client x1-runner (realm-management manage-users alone, created by the runbook, never
// in a realm file), and deletes them afterwards, however the run ends. E2E_X1_CLIENT_SECRET, x1-runner's secret, says
// which: set, the test creates them.

const keycloak = process.env.E2E_KEYCLOAK_URL ?? 'http://localhost:8080';
const clientId = 'x1-runner';
const clientSecret = process.env.E2E_X1_CLIENT_SECRET ?? '';
const admin = `${keycloak}/admin/realms/pharma-training`;

export type X1User = {
  username: string;
  password: string;
  email: string;
  firstName: string;
  lastName: string;
};

/** The three people of the flow, and how to remove them once it ends: nothing for the local users. */
export type X1Cast = {
  owner: X1User;
  editor: X1User;
  employee: X1User;
  dispose: () => Promise<void>;
};

/** The name the app shows for a user: their first and last name. */
export const fullName = (user: X1User) => `${user.firstName} ${user.lastName}`;

const local = (username: string, firstName: string, lastName: string): X1User => ({
  username,
  password: username,
  email: `${username}@example.com`,
  firstName,
  lastName,
});

/** The cast for this run: the realm's local users, or, with x1-runner's secret, three users of the run's own. */
export async function x1Cast(request: APIRequestContext): Promise<X1Cast> {
  if (clientSecret === '') {
    return {
      owner: local('owner', 'Olaf', 'Owner'),
      editor: local('editor', 'Emma', 'Editor'),
      // A user no other spec invites: an address is mailed at most every 10 minutes (#265).
      employee: local('employee-c', 'Clara', 'Employee'),
      dispose: async () => {},
    };
  }

  const run = unique('x1').toLowerCase();
  const created: string[] = [];
  // Every user made is deleted, even when deleting another fails; a fresh token, since the flow outlasts one.
  const dispose = async () => {
    if (created.length === 0) {
      return;
    }
    const token = await adminToken(request);
    const failed: string[] = [];
    for (const id of created) {
      const deleted = await request.delete(`${admin}/users/${id}`, { headers: { Authorization: token } });
      if (![204, 404].includes(deleted.status())) {
        failed.push(`${id} (${deleted.status()})`);
      }
    }
    expect(failed, `these users were not deleted: ${failed.join(', ')}`).toEqual([]);
  };
  try {
    const token = await adminToken(request);
    const make = async (role: string, firstName: string, lastName: string, realmRole?: string) => {
      const user: X1User = {
        username: `${run}-${role}`,
        password: randomBytes(24).toString('base64url'),
        email: `${run}-${role}@example.com`,
        firstName,
        lastName,
      };
      created.push(await createUser(request, token, user));
      if (realmRole !== undefined) {
        await grantRealmRole(request, token, created.at(-1) ?? '', realmRole);
      }
      return user;
    };
    return {
      owner: await make('owner', 'Olaf', 'Owner'),
      editor: await make('editor', 'Emma', 'Editor', 'editor'),
      employee: await make('employee', 'Clara', 'Employee'),
      dispose,
    };
  } catch (error) {
    // A set-up that failed halfway leaves none of the users it made.
    await dispose();
    throw error;
  }
}

/** x1-runner's access token, as an Authorization header. */
async function adminToken(request: APIRequestContext) {
  const response = await request.post(`${keycloak}/realms/pharma-training/protocol/openid-connect/token`, {
    form: { grant_type: 'client_credentials', client_id: clientId, client_secret: clientSecret },
  });
  expect(response.ok(), `x1-runner's token answered ${response.status()}`).toBe(true);
  return `Bearer ${((await response.json()) as { access_token: string }).access_token}`;
}

/** Creates the user, enabled, with a verified address and a password that never expires; answers their id. */
async function createUser(request: APIRequestContext, token: string, user: X1User) {
  const response = await request.post(`${admin}/users`, {
    headers: { Authorization: token },
    data: {
      username: user.username,
      email: user.email,
      firstName: user.firstName,
      lastName: user.lastName,
      enabled: true,
      emailVerified: true,
      requiredActions: [],
      credentials: [{ type: 'password', value: user.password, temporary: false }],
    },
  });
  expect(response.status(), `creating ${user.username} answered ${response.status()}: ${await response.text()}`).toBe(
    201,
  );
  const id = (response.headers()['location'] ?? '').split('/').at(-1) ?? '';
  expect(id, `Keycloak named no id for ${user.username}`).not.toBe('');
  return id;
}

/**
 * Grants a realm role, read from the roles the user may still be given: that list asks manage-users alone, where
 * reading a role by name would ask view-realm too.
 */
async function grantRealmRole(request: APIRequestContext, token: string, id: string, name: string) {
  const available = await request.get(`${admin}/users/${id}/role-mappings/realm/available`, {
    headers: { Authorization: token },
  });
  expect(available.ok(), `the roles user ${id} may be given answered ${available.status()}`).toBe(true);
  const role = ((await available.json()) as { id: string; name: string }[]).find((each) => each.name === name);
  expect(role, `the realm role ${name} cannot be given to user ${id}`).toBeDefined();
  const granted = await request.post(`${admin}/users/${id}/role-mappings/realm`, {
    headers: { Authorization: token },
    data: [role],
  });
  expect(granted.status(), `granting ${name} answered ${granted.status()}`).toBe(204);
}
